using RendezvousNET.Domain.Common;

namespace RendezvousNET.Domain.Entities;

/// <summary>
/// Registered application user (host or participant).
/// Credentials are managed by ASP.NET Core Identity.
/// </summary>
public sealed class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Navigation
    public ICollection<Meeting> HostedMeetings { get; set; } = [];
    public ICollection<MeetingParticipant> Participations { get; set; } = [];
}
