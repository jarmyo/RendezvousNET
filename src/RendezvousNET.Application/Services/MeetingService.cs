using Microsoft.EntityFrameworkCore;
using RendezvousNET.Application.Common;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Shared.DTOs.Meetings;
using RendezvousNET.Shared.DTOs.Auth;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Application.Services;

public sealed class MeetingService : IMeetingService
{
    private readonly IAppDbContext _db;

    public MeetingService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<MeetingDto>> CreateAsync(
        CreateMeetingRequestDto request,
        Guid hostId,
        CancellationToken cancellationToken = default)
    {
        var host = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == hostId, cancellationToken);

        if (host is null)
            return Result<MeetingDto>.Failure("Host user not found.");

        var meeting = new Meeting
        {
            Title = request.Title,
            Description = request.Description,
            Location = request.Location,
            LocationLatitude = request.LocationLatitude,
            LocationLongitude = request.LocationLongitude,
            ScheduledAt = request.ScheduledAt,
            HostId = hostId,
            InviteCode = GenerateInviteCode()
        };

        _db.Add(meeting);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<MeetingDto>.Success(ToDto(meeting, host, []));
    }

    public async Task<Result<MeetingDto>> GetByIdAsync(
        Guid meetingId,
        CancellationToken cancellationToken = default)
    {
        var meeting = await _db.Meetings
            .Include(m => m.Host)
            .Include(m => m.Participants)
                .ThenInclude(p => p.User)
            .Include(m => m.Participants)
                .ThenInclude(p => p.TrustedFriend)
            .FirstOrDefaultAsync(m => m.Id == meetingId, cancellationToken);

        if (meeting is null)
            return Result<MeetingDto>.Failure("Meeting not found.");

        return Result<MeetingDto>.Success(ToDto(meeting, meeting.Host, meeting.Participants));
    }

    public async Task<Result<MeetingDto>> GetByInviteCodeAsync(
        string inviteCode,
        CancellationToken cancellationToken = default)
    {
        var meeting = await _db.Meetings
            .Include(m => m.Host)
            .Include(m => m.Participants)
                .ThenInclude(p => p.User)
            .Include(m => m.Participants)
                .ThenInclude(p => p.TrustedFriend)
            .FirstOrDefaultAsync(m => m.InviteCode == inviteCode, cancellationToken);

        if (meeting is null)
            return Result<MeetingDto>.Failure("Meeting not found.");

        return Result<MeetingDto>.Success(ToDto(meeting, meeting.Host, meeting.Participants));
    }

    public async Task<Result<IReadOnlyList<MeetingSummaryDto>>> GetByHostAsync(
        Guid hostId,
        CancellationToken cancellationToken = default)
    {
        var meetings = await _db.Meetings
            .Where(m => m.HostId == hostId)
            .Include(m => m.Participants)
            .OrderByDescending(m => m.ScheduledAt)
            .ToListAsync(cancellationToken);

        var summaries = meetings.Select(m => new MeetingSummaryDto
        {
            Id = m.Id,
            Title = m.Title,
            Location = m.Location,
            ScheduledAt = m.ScheduledAt,
            Status = m.Status,
            InviteCode = m.InviteCode,
            ParticipantCount = m.Participants.Count
        }).ToList();

        return Result<IReadOnlyList<MeetingSummaryDto>>.Success(summaries);
    }

    public async Task<Result> CancelAsync(
        Guid meetingId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, cancellationToken);

        if (meeting is null)
            return Result.Failure("Meeting not found.");

        if (meeting.HostId != requestingUserId)
            return Result.Failure("Only the host can cancel the meeting.");

        if (meeting.Status == MeetingStatus.Completed)
            return Result.Failure("Cannot cancel a completed meeting.");

        meeting.Status = MeetingStatus.Cancelled;
        meeting.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // --- Private helpers ---

    private static string GenerateInviteCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private static MeetingDto ToDto(
        Meeting meeting,
        User host,
        IEnumerable<MeetingParticipant> participants)
    {
        return new MeetingDto
        {
            Id = meeting.Id,
            Title = meeting.Title,
            Description = meeting.Description,
            Location = meeting.Location,
            LocationLatitude = meeting.LocationLatitude,
            LocationLongitude = meeting.LocationLongitude,
            ScheduledAt = meeting.ScheduledAt,
            Status = meeting.Status,
            InviteCode = meeting.InviteCode,
            Host = new UserProfileDto
            {
                Id = host.Id,
                Email = host.Email,
                DisplayName = host.DisplayName
            },
            Participants = participants.Select(p => new Shared.DTOs.Participants.MeetingParticipantDto
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                ParticipantType = p.ParticipantType,
                Status = p.Status,
                CurrentLatitude = p.CurrentLatitude,
                CurrentLongitude = p.CurrentLongitude,
                JoinedAt = p.CreatedAt,
                TrustedFriend = p.TrustedFriend is null ? null : new Shared.DTOs.Participants.TrustedFriendDto
                {
                    Id = p.TrustedFriend.Id,
                    Email = p.TrustedFriend.Email,
                    DisplayName = p.TrustedFriend.DisplayName
                }
            }).ToList(),
            CreatedAt = meeting.CreatedAt
        };
    }
}
