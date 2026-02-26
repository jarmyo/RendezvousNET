using RendezvousNET.Domain.Common;

namespace RendezvousNET.Domain.Entities;

/// <summary>
/// A trusted-friend record linked to exactly one <see cref="MeetingParticipant"/>.
/// Only notified when their specific participant triggers an emergency.
/// </summary>
public sealed class TrustedFriend : BaseEntity
{
    public Guid ParticipantId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>FCM/APNs device token for push notifications (optional).</summary>
    public string? DeviceToken { get; set; }

    // Navigation
    public MeetingParticipant Participant { get; set; } = null!;
}
