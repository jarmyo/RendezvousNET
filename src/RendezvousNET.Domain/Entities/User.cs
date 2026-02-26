namespace RendezvousNET.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Meeting> HostedMeetings { get; set; } = [];
    public ICollection<MeetingParticipant> Participations { get; set; } = [];
}
