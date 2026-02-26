namespace RendezvousNET.Domain.Entities;

public class TrustedFriend
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public MeetingParticipant Participant { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsNotified { get; set; }
    public string? DeviceToken { get; set; }
    public string? WebPushSubscription { get; set; }
}
