using RendezvousNET.Shared.DTOs.Notifications;

namespace RendezvousNET.Application.Interfaces;

/// <summary>
/// Abstraction for sending notifications across different channels.
/// Implementations choose the best channel per recipient and fall back as needed.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a notification to a recipient. 
    /// Channel selection priority:
    ///   1. Push (FCM/APNs) if <see cref="SendNotificationRequestDto.RecipientDeviceToken"/> is provided.
    ///   2. Web Push if the recipient has a Web Push subscription registered.
    ///   3. Transactional email (SendGrid) as the final fallback — crucial for iOS Safari.
    /// </summary>
    Task<bool> SendAsync(SendNotificationRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers or updates a Web Push subscription for a given user.
    /// </summary>
    Task RegisterWebPushSubscriptionAsync(string userEmail, string endpoint, string p256dh, string auth, CancellationToken cancellationToken = default);

    // TODO: Implementar proveedor de WhatsApp para futura monetización
}
