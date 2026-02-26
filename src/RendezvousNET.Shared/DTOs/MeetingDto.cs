using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs;

public class MeetingDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Location { get; set; } = string.Empty;
    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public MeetingStatus Status { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string InviteCode { get; set; } = string.Empty;
    public List<MeetingParticipantDto> Participants { get; set; } = [];
}
