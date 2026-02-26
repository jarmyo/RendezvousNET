using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs;

public class MeetingParticipantDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public ParticipantStatus Status { get; set; }
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public DateTimeOffset? CheckInAt { get; set; }
    public TrustedFriendDto? TrustedFriend { get; set; }
}
