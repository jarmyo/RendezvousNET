using Microsoft.Extensions.Logging;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Shared.DTOs.Notifications;

namespace RendezvousNET.Infrastructure.Notifications;

/// <summary>
/// Web Push subscription details stored per user.
/// </summary>
public sealed class WebPushSubscription
{
    public string UserEmail { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

/// <summary>
/// Composite notification service implementing the zero-cost-initial strategy:
///
///   Priority 1 — Native Push (FCM/APNs): when a device token is present.
///   Priority 2 — Web Push: when a web-push subscription exists for the recipient.
///   Priority 3 — Transactional Email (SendGrid): mandatory fallback,
///                especially crucial for iOS Safari which does not support Web Push.
///
/// Each inner service is injected as an optional dependency so the system degrades
/// gracefully if a provider is not configured.
/// </summary>
public sealed class CompositeNotificationService : INotificationService
{
    private readonly IPushNotificationProvider? _pushProvider;
    private readonly IWebPushProvider? _webPushProvider;
    private readonly IEmailNotificationProvider? _emailProvider;
    private readonly ILogger<CompositeNotificationService> _logger;

    // In-memory store of Web Push subscriptions (replace with DB in production)
    private readonly Dictionary<string, WebPushSubscription> _webPushSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    public CompositeNotificationService(
        ILogger<CompositeNotificationService> logger,
        IPushNotificationProvider? pushProvider = null,
        IWebPushProvider? webPushProvider = null,
        IEmailNotificationProvider? emailProvider = null)
    {
        _logger = logger;
        _pushProvider = pushProvider;
        _webPushProvider = webPushProvider;
        _emailProvider = emailProvider;
    }

    public async Task<bool> SendAsync(
        SendNotificationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // 1. Try native push (FCM/APNs)
        if (_pushProvider is not null && !string.IsNullOrEmpty(request.RecipientDeviceToken))
        {
            try
            {
                var sent = await _pushProvider.SendPushAsync(request, cancellationToken);
                if (sent)
                {
                    _logger.LogInformation("Push notification sent to device token for {Email}.", request.RecipientEmail);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push notification failed for {Email}. Falling back.", request.RecipientEmail);
            }
        }

        // 2. Try Web Push
        if (_webPushProvider is not null &&
            _webPushSubscriptions.TryGetValue(request.RecipientEmail, out var subscription))
        {
            try
            {
                var sent = await _webPushProvider.SendWebPushAsync(subscription, request, cancellationToken);
                if (sent)
                {
                    _logger.LogInformation("Web Push notification sent to {Email}.", request.RecipientEmail);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web Push failed for {Email}. Falling back to email.", request.RecipientEmail);
            }
        }

        // 3. Transactional email fallback (SendGrid) — critical for iOS Safari
        if (_emailProvider is not null)
        {
            try
            {
                var sent = await _emailProvider.SendEmailAsync(request, cancellationToken);
                if (sent)
                {
                    _logger.LogInformation("Transactional email sent to {Email}.", request.RecipientEmail);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email notification also failed for {Email}.", request.RecipientEmail);
            }
        }

        _logger.LogError("All notification channels failed for {Email}.", request.RecipientEmail);
        return false;
    }

    public Task RegisterWebPushSubscriptionAsync(
        string userEmail,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken = default)
    {
        _webPushSubscriptions[userEmail] = new WebPushSubscription
        {
            UserEmail = userEmail,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth
        };

        _logger.LogInformation("Web Push subscription registered for {Email}.", userEmail);
        return Task.CompletedTask;
    }

    // TODO: Implementar proveedor de WhatsApp para futura monetización
}
