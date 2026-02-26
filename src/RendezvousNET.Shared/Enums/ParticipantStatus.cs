namespace RendezvousNET.Shared.Enums;

/// <summary>
/// Status of a meeting participant shown as colored circles in the UI.
/// Gray=Pending, Green=Arrived, Yellow=Delayed, Red=Emergency
/// </summary>
public enum ParticipantStatus
{
    Pending = 0,    // Gray - En camino / Pendiente
    Arrived = 1,    // Green - Ya estoy en el lugar
    Delayed = 2,    // Yellow - Estoy demorado (shares temporary location)
    Emergency = 3   // Red - Emergencia
}
