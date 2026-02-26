using RendezvousNET.Domain.Common;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Domain.Entities;

/// <summary>
/// Represents one participant slot inside a meeting.
/// Supports both registered users and anonymous guests.
/// </summary>
public sealed class MeetingParticipant : BaseEntity
{
    // FK to meeting (required)
    public Guid MeetingId { get; set; }

    // FK to registered user (null for anonymous guests)
    public Guid? UserId { get; set; }

    /// <summary>Display name used when no authenticated user is linked.</summary>
    public string? GuestDisplayName { get; set; }

    public ParticipantType ParticipantType { get; set; } = ParticipantType.RegisteredUser;
    public ParticipantStatus Status { get; set; } = ParticipantStatus.Pending;

    /// <summary>Live GPS latitude, set when Status == Delayed.</summary>
    public double? CurrentLatitude { get; set; }

    /// <summary>Live GPS longitude, set when Status == Delayed.</summary>
    public double? CurrentLongitude { get; set; }

    public DateTimeOffset? CheckInRequestedAt { get; set; }

    // Navigation
    public Meeting Meeting { get; set; } = null!;
    public User? User { get; set; }
    public TrustedFriend? TrustedFriend { get; set; }

    /// <summary>Resolved display name regardless of participant type.</summary>
    public string DisplayName =>
        User?.DisplayName ?? GuestDisplayName ?? "Unknown";
}
