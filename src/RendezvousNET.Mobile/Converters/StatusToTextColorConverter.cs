using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Mobile.Converters;

/// <summary>
/// Converts ParticipantStatus to a text color hex string.
/// Full implementation requires MAUI workload (IValueConverter from Microsoft.Maui.Controls).
/// </summary>
public class StatusToTextColorConverter
{
    public static string GetTextColorHex(ParticipantStatus status) => status switch
    {
        ParticipantStatus.Arrived   => "#16A34A",
        ParticipantStatus.Delayed   => "#A16207",
        ParticipantStatus.Emergency => "#DC2626",
        _                           => "#6B7280"
    };
}
