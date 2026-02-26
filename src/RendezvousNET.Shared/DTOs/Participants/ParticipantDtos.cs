using System.ComponentModel.DataAnnotations;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs.Participants;

public sealed record JoinMeetingRequestDto
{
    /// <summary>The 8-character invite code for the meeting.</summary>
    [Required, Length(8, 8)]
    public string InviteCode { get; init; } = string.Empty;

    /// <summary>Display name for anonymous guests. Ignored for authenticated users.</summary>
    [MaxLength(100)]
    public string? GuestDisplayName { get; init; }

    /// <summary>Optional trusted-friend email provided at join time.</summary>
    [EmailAddress, MaxLength(256)]
    public string? TrustedFriendEmail { get; init; }

    /// <summary>Optional trusted-friend display name.</summary>
    [MaxLength(100)]
    public string? TrustedFriendDisplayName { get; init; }
}

public sealed record MeetingParticipantDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ParticipantType ParticipantType { get; init; }
    public ParticipantStatus Status { get; init; }
    public double? CurrentLatitude { get; init; }
    public double? CurrentLongitude { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public TrustedFriendDto? TrustedFriend { get; init; }
}

public sealed record TrustedFriendDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record UpdateParticipantStatusRequestDto
{
    [Required]
    public ParticipantStatus Status { get; init; }

    /// <summary>Required when Status is Delayed — shares temporary GPS location.</summary>
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}

/// <summary>
/// SignalR real-time payload broadcast to all clients in the meeting group
/// when a participant's status changes.
/// </summary>
public sealed record ParticipantStatusUpdatedDto
{
    public Guid MeetingId { get; init; }
    public Guid ParticipantId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ParticipantStatus Status { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
