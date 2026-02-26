using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Mobile.Converters;

/// <summary>
/// Returns true if the status is Emergency.
/// Full implementation requires MAUI workload (IValueConverter from Microsoft.Maui.Controls).
/// </summary>
public class IsEmergencyConverter
{
    public static bool IsEmergency(ParticipantStatus status) => status == ParticipantStatus.Emergency;
}
