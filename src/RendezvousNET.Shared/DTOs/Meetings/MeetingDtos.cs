using System.ComponentModel.DataAnnotations;
using RendezvousNET.Shared.DTOs.Auth;
using RendezvousNET.Shared.DTOs.Participants;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs.Meetings;

public sealed record CreateMeetingRequestDto
{
    [Required, MaxLength(200)]
    public string Title { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; init; }

    [Required, MaxLength(300)]
    public string Location { get; init; } = string.Empty;

    public double? LocationLatitude { get; init; }
    public double? LocationLongitude { get; init; }

    [Required]
    public DateTimeOffset ScheduledAt { get; init; }
}

public sealed record MeetingDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Location { get; init; } = string.Empty;
    public double? LocationLatitude { get; init; }
    public double? LocationLongitude { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public MeetingStatus Status { get; init; }
    public string InviteCode { get; init; } = string.Empty;
    public UserProfileDto Host { get; init; } = null!;
    public IReadOnlyList<MeetingParticipantDto> Participants { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record MeetingSummaryDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; init; }
    public MeetingStatus Status { get; init; }
    public string InviteCode { get; init; } = string.Empty;
    public int ParticipantCount { get; init; }
}
