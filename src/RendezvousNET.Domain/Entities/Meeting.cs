using RendezvousNET.Domain.Common;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Domain.Entities;

/// <summary>
/// A meeting (rendezvous) created by a host.
/// One meeting has many <see cref="MeetingParticipant"/> records.
/// </summary>
public sealed class Meeting : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Location { get; set; } = string.Empty;
    public double? LocationLatitude { get; set; }
    public double? LocationLongitude { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;

    /// <summary>8-character alphanumeric code used to invite participants.</summary>
    public string InviteCode { get; set; } = string.Empty;

    // Foreign key
    public Guid HostId { get; set; }

    // Navigation
    public User Host { get; set; } = null!;
    public ICollection<MeetingParticipant> Participants { get; set; } = [];
}
