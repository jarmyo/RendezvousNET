namespace RendezvousNET.Shared.DTOs.Notifications;

public sealed record SendNotificationRequestDto
{
    public string RecipientEmail { get; init; } = string.Empty;
    public string? RecipientDeviceToken { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public Dictionary<string, string> Data { get; init; } = [];
}
