using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Domain.Entities;

public class MeetingParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public ParticipantStatus Status { get; set; } = ParticipantStatus.Pending;
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public DateTimeOffset? CheckInAt { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public TrustedFriend? TrustedFriend { get; set; }
}
