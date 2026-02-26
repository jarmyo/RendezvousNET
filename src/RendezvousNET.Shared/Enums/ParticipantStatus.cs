namespace RendezvousNET.Shared.Enums;

/// <summary>
/// Real-time status of a meeting participant.
/// Drives the color circle shown in the dashboard.
/// </summary>
public enum ParticipantStatus
{
    /// <summary>Gray circle — pending / on the way.</summary>
    Pending = 0,

    /// <summary>Green circle — already at the meeting place.</summary>
    Arrived = 1,

    /// <summary>Yellow circle — running late (shares temporary location).</summary>
    Delayed = 2,

    /// <summary>Red circle — emergency state.</summary>
    Emergency = 3
}
