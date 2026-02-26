using RendezvousNET.Application.Interfaces;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Domain.Interfaces;
using RendezvousNET.Shared.DTOs;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Application.Services;

public class MeetingService(
    IMeetingRepository meetingRepo,
    IParticipantRepository participantRepo,
    INotificationService notificationService,
    IMeetingStatusHub statusHub)
{
    public async Task<MeetingDto> CreateMeetingAsync(Guid hostId, CreateMeetingDto dto, CancellationToken ct = default)
    {
        var meeting = new Meeting
        {
            HostId = hostId,
            Title = dto.Title,
            Description = dto.Description,
            Location = dto.Location,
            LocationLatitude = dto.LocationLatitude,
            LocationLongitude = dto.LocationLongitude,
            ScheduledAt = dto.ScheduledAt,
            InviteCode = GenerateInviteCode()
        };

        await meetingRepo.AddAsync(meeting, ct);
        return MapToDto(meeting);
    }

    public async Task<MeetingParticipantDto> JoinMeetingAsync(Guid? userId, JoinMeetingDto dto, CancellationToken ct = default)
    {
        var meeting = await meetingRepo.GetByInviteCodeAsync(dto.InviteCode, ct)
            ?? throw new InvalidOperationException("Meeting not found with given invite code.");

        var participant = new MeetingParticipant
        {
            MeetingId = meeting.Id,
            UserId = userId,
            DisplayName = dto.DisplayName ?? "Guest",
            IsAnonymous = userId is null,
            Status = ParticipantStatus.Pending
        };

        if (!string.IsNullOrWhiteSpace(dto.TrustedFriendEmail))
        {
            participant.TrustedFriend = new TrustedFriend
            {
                ParticipantId = participant.Id,
                Name = dto.TrustedFriendName ?? "Trusted Friend",
                Email = dto.TrustedFriendEmail,
                Phone = dto.TrustedFriendPhone
            };
        }

        await participantRepo.AddAsync(participant, ct);
        return MapParticipantToDto(participant);
    }

    public async Task UpdateParticipantStatusAsync(UpdateParticipantStatusDto dto, CancellationToken ct = default)
    {
        var participant = await participantRepo.GetByIdAsync(dto.ParticipantId, ct)
            ?? throw new InvalidOperationException("Participant not found.");

        participant.Status = dto.Status;

        if (dto.Status == ParticipantStatus.Delayed)
        {
            participant.CurrentLatitude = dto.CurrentLatitude;
            participant.CurrentLongitude = dto.CurrentLongitude;
        }

        if (dto.Status == ParticipantStatus.Arrived)
            participant.CheckInAt = DateTimeOffset.UtcNow;

        await participantRepo.UpdateAsync(participant, ct);

        // Broadcast real-time status update via SignalR
        var update = new ParticipantStatusUpdatedDto
        {
            MeetingId = participant.MeetingId,
            ParticipantId = participant.Id,
            Status = participant.Status,
            CurrentLatitude = participant.CurrentLatitude,
            CurrentLongitude = participant.CurrentLongitude,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await statusHub.BroadcastStatusUpdateAsync(update);

        // Notify trusted friend only on Emergency
        if (dto.Status == ParticipantStatus.Emergency && participant.TrustedFriend is not null)
            await notificationService.NotifyTrustedFriendEmergencyAsync(participant.Id, ct);
    }

    private static string GenerateInviteCode() =>
        Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static MeetingDto MapToDto(Meeting m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Description = m.Description,
        Location = m.Location,
        LocationLatitude = m.LocationLatitude,
        LocationLongitude = m.LocationLongitude,
        ScheduledAt = m.ScheduledAt,
        Status = m.Status,
        HostName = m.Host?.DisplayName ?? string.Empty,
        InviteCode = m.InviteCode,
        Participants = m.Participants?.Select(MapParticipantToDto).ToList() ?? []
    };

    private static MeetingParticipantDto MapParticipantToDto(MeetingParticipant p) => new()
    {
        Id = p.Id,
        MeetingId = p.MeetingId,
        DisplayName = p.DisplayName,
        IsAnonymous = p.IsAnonymous,
        Status = p.Status,
        CurrentLatitude = p.CurrentLatitude,
        CurrentLongitude = p.CurrentLongitude,
        CheckInAt = p.CheckInAt,
        TrustedFriend = p.TrustedFriend is null ? null : new TrustedFriendDto
        {
            Id = p.TrustedFriend.Id,
            Name = p.TrustedFriend.Name,
            Email = p.TrustedFriend.Email,
            Phone = p.TrustedFriend.Phone,
            IsNotified = p.TrustedFriend.IsNotified
        }
    };
}
