using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Mobile.Converters;

/// <summary>
/// Converts ParticipantStatus to a color string for the status circle.
/// Gray=Pending, Green=Arrived, Yellow=Delayed, Red=Emergency
/// Full implementation requires MAUI workload (IValueConverter from Microsoft.Maui.Controls).
/// </summary>
public class StatusToColorConverter
{
    public static string GetColorHex(ParticipantStatus status) => status switch
    {
        ParticipantStatus.Arrived   => "#22C55E",   // Green
        ParticipantStatus.Delayed   => "#EAB308",   // Yellow
        ParticipantStatus.Emergency => "#EF4444",   // Red
        _                           => "#9CA3AF"    // Gray
    };
}
