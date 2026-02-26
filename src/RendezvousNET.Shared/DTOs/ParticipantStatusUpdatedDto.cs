using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs;

/// <summary>
/// Real-time SignalR payload for participant status circle color updates.
/// </summary>
public class ParticipantStatusUpdatedDto
{
    public Guid MeetingId { get; set; }
    public Guid ParticipantId { get; set; }
    public ParticipantStatus Status { get; set; }
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
