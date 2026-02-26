using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Mobile.Converters;

/// <summary>
/// Converts ParticipantStatus to a localized label string.
/// Full implementation requires MAUI workload (IValueConverter from Microsoft.Maui.Controls).
/// </summary>
public class StatusToLabelConverter
{
    public static string GetLabel(ParticipantStatus status) => status switch
    {
        ParticipantStatus.Arrived   => "Ya estoy en el lugar ✅",
        ParticipantStatus.Delayed   => "Estoy demorado 🟡",
        ParticipantStatus.Emergency => "¡EMERGENCIA! 🆘",
        _                           => "En camino..."
    };
}
