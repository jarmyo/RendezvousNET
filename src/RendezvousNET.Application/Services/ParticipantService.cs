using Microsoft.EntityFrameworkCore;
using RendezvousNET.Application.Common;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Shared.DTOs.Participants;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Application.Services;

public sealed class ParticipantService : IParticipantService
{
    private readonly IAppDbContext _db;
    private readonly INotificationService _notifications;

    public ParticipantService(IAppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<Result<MeetingParticipantDto>> JoinMeetingAsync(
        JoinMeetingRequestDto request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.InviteCode == request.InviteCode, cancellationToken);

        if (meeting is null)
            return Result<MeetingParticipantDto>.Failure("Invalid invite code.");

        if (meeting.Status == MeetingStatus.Cancelled || meeting.Status == MeetingStatus.Completed)
            return Result<MeetingParticipantDto>.Failure("This meeting is no longer active.");

        // Prevent duplicate registrations for authenticated users
        if (userId.HasValue)
        {
            var existing = await _db.MeetingParticipants
                .AnyAsync(p => p.MeetingId == meeting.Id && p.UserId == userId, cancellationToken);

            if (existing)
                return Result<MeetingParticipantDto>.Failure("You have already joined this meeting.");
        }

        User? user = null;
        if (userId.HasValue)
        {
            user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
            if (user is null)
                return Result<MeetingParticipantDto>.Failure("User not found.");
        }

        var participant = new MeetingParticipant
        {
            MeetingId = meeting.Id,
            UserId = userId,
            GuestDisplayName = userId.HasValue ? null : request.GuestDisplayName,
            ParticipantType = userId.HasValue ? ParticipantType.RegisteredUser : ParticipantType.AnonymousGuest,
            Status = ParticipantStatus.Pending
        };

        _db.Add(participant);

        // Optionally register a trusted friend
        if (!string.IsNullOrWhiteSpace(request.TrustedFriendEmail))
        {
            var trustedFriend = new TrustedFriend
            {
                ParticipantId = participant.Id,
                Email = request.TrustedFriendEmail,
                DisplayName = request.TrustedFriendDisplayName ?? request.TrustedFriendEmail
            };
            _db.Add(trustedFriend);
            participant.TrustedFriend = trustedFriend;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result<MeetingParticipantDto>.Success(ToDto(participant));
    }

    public async Task<Result<ParticipantStatusUpdatedDto>> UpdateStatusAsync(
        Guid participantId,
        UpdateParticipantStatusRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var participant = await _db.MeetingParticipants
            .Include(p => p.TrustedFriend)
            .Include(p => p.Meeting)
            .FirstOrDefaultAsync(p => p.Id == participantId, cancellationToken);

        if (participant is null)
            return Result<ParticipantStatusUpdatedDto>.Failure("Participant not found.");

        if (request.Status == ParticipantStatus.Delayed &&
            (request.Latitude is null || request.Longitude is null))
        {
            return Result<ParticipantStatusUpdatedDto>.Failure(
                "Latitude and Longitude are required when status is Delayed.");
        }

        participant.Status = request.Status;
        participant.CurrentLatitude = request.Latitude;
        participant.CurrentLongitude = request.Longitude;
        participant.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Notify trusted friend on emergency
        if (request.Status == ParticipantStatus.Emergency && participant.TrustedFriend is not null)
        {
            await _notifications.SendAsync(new Shared.DTOs.Notifications.SendNotificationRequestDto
            {
                RecipientEmail = participant.TrustedFriend.Email,
                RecipientDeviceToken = participant.TrustedFriend.DeviceToken,
                Subject = "🚨 Emergency Alert",
                Body = $"{participant.DisplayName} has triggered an emergency at {participant.Meeting.Location}.",
                Data = new Dictionary<string, string>
                {
                    ["meetingId"] = participant.MeetingId.ToString(),
                    ["participantId"] = participant.Id.ToString(),
                    ["hostLocation"] = participant.Meeting.Location
                }
            }, cancellationToken);
        }

        var payload = new ParticipantStatusUpdatedDto
        {
            MeetingId = participant.MeetingId,
            ParticipantId = participant.Id,
            DisplayName = participant.DisplayName,
            Status = participant.Status,
            Latitude = participant.CurrentLatitude,
            Longitude = participant.CurrentLongitude,
            UpdatedAt = participant.UpdatedAt ?? DateTimeOffset.UtcNow
        };

        return Result<ParticipantStatusUpdatedDto>.Success(payload);
    }

    public async Task<Result<MeetingParticipantDto>> GetByIdAsync(
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _db.MeetingParticipants
            .Include(p => p.User)
            .Include(p => p.TrustedFriend)
            .FirstOrDefaultAsync(p => p.Id == participantId, cancellationToken);

        if (participant is null)
            return Result<MeetingParticipantDto>.Failure("Participant not found.");

        return Result<MeetingParticipantDto>.Success(ToDto(participant));
    }

    public async Task ProcessCheckInRequestsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var checkInWindow = TimeSpan.FromMinutes(5);

        // Find meetings that started within the check-in window
        var participants = await _db.MeetingParticipants
            .Include(p => p.Meeting)
            .Include(p => p.TrustedFriend)
            .Where(p =>
                p.Status == ParticipantStatus.Pending &&
                p.Meeting.ScheduledAt <= now &&
                p.Meeting.ScheduledAt >= now - checkInWindow &&
                p.CheckInRequestedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var participant in participants)
        {
            participant.CheckInRequestedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static MeetingParticipantDto ToDto(MeetingParticipant p) =>
        new()
        {
            Id = p.Id,
            DisplayName = p.DisplayName,
            ParticipantType = p.ParticipantType,
            Status = p.Status,
            CurrentLatitude = p.CurrentLatitude,
            CurrentLongitude = p.CurrentLongitude,
            JoinedAt = p.CreatedAt,
            TrustedFriend = p.TrustedFriend is null ? null : new TrustedFriendDto
            {
                Id = p.TrustedFriend.Id,
                Email = p.TrustedFriend.Email,
                DisplayName = p.TrustedFriend.DisplayName
            }
        };
}
