using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Domain.Entities;

public class Meeting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Location { get; set; } = string.Empty;
    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;
    public string InviteCode { get; set; } = string.Empty;
    public Guid HostId { get; set; }
    public User Host { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<MeetingParticipant> Participants { get; set; } = [];
}
