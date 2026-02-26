using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Shared.DTOs;

public class NotificationDto
{
    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientDeviceToken { get; set; }
    public string? RecipientWebPushSubscription { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationChannel PreferredChannel { get; set; }
    public Dictionary<string, string> Data { get; set; } = [];
}
