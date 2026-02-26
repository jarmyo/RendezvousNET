using RendezvousNET.Shared.DTOs.Notifications;

namespace RendezvousNET.Infrastructure.Notifications;

/// <summary>Abstraction for native push providers (FCM / APNs).</summary>
public interface IPushNotificationProvider
{
    Task<bool> SendPushAsync(SendNotificationRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>Abstraction for browser Web Push (VAPID).</summary>
public interface IWebPushProvider
{
    Task<bool> SendWebPushAsync(WebPushSubscription subscription, SendNotificationRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>Abstraction for transactional email (e.g. SendGrid).</summary>
public interface IEmailNotificationProvider
{
    Task<bool> SendEmailAsync(SendNotificationRequestDto request, CancellationToken cancellationToken = default);
}
