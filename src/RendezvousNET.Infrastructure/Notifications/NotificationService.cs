using Microsoft.Extensions.Logging;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Domain.Interfaces;
using RendezvousNET.Shared.DTOs;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Infrastructure.Notifications;

/// <summary>
/// Notification service with channel fallback strategy:
/// - App installed → Native Push (FCM/APNs)
/// - Web only → Web Push first, fallback to Email (critical for iOS Safari)
/// - Emergency → also notifies Trusted Friend via best available channel
/// TODO: Implementar proveedor de WhatsApp para futura monetización
/// </summary>
public class NotificationService(
    ILogger<NotificationService> logger,
    IParticipantRepository participantRepo,
    IMeetingRepository meetingRepo) : INotificationService
{
    public async Task SendAsync(NotificationDto notification, CancellationToken ct = default)
    {
        bool sent = false;

        if (notification.PreferredChannel == NotificationChannel.Push && !string.IsNullOrEmpty(notification.RecipientDeviceToken))
        {
            sent = await TrySendPushAsync(notification, ct);
        }

        if (!sent && !string.IsNullOrEmpty(notification.RecipientWebPushSubscription))
        {
            sent = await TrySendWebPushAsync(notification, ct);
        }

        // Fallback to email - crucial for iOS Safari where Web Push is unreliable
        if (!sent)
        {
            await SendEmailAsync(notification, ct);
        }
    }

    public async Task NotifyTrustedFriendEmergencyAsync(Guid participantId, CancellationToken ct = default)
    {
        var participant = await participantRepo.GetByIdAsync(participantId, ct);
        if (participant?.TrustedFriend is null) return;

        var meeting = await meetingRepo.GetByIdAsync(participant.MeetingId, ct);
        if (meeting is null) return;

        var notification = new NotificationDto
        {
            RecipientEmail = participant.TrustedFriend.Email,
            RecipientDeviceToken = participant.TrustedFriend.DeviceToken,
            RecipientWebPushSubscription = participant.TrustedFriend.WebPushSubscription,
            Title = "⚠️ EMERGENCIA - Acción requerida",
            Body = $"{participant.DisplayName} ha activado una alerta de emergencia. " +
                   $"Cita en: {meeting.Location}. Anfitrión: {meeting.Host?.DisplayName}.",
            PreferredChannel = !string.IsNullOrEmpty(participant.TrustedFriend.DeviceToken)
                ? NotificationChannel.Push
                : NotificationChannel.Email,
            Data = new Dictionary<string, string>
            {
                ["meetingId"] = meeting.Id.ToString(),
                ["participantId"] = participantId.ToString(),
                ["type"] = "emergency",
                ["hostName"] = meeting.Host?.DisplayName ?? string.Empty,
                ["location"] = meeting.Location,
                ["latitude"] = meeting.LocationLatitude?.ToString() ?? string.Empty,
                ["longitude"] = meeting.LocationLongitude?.ToString() ?? string.Empty
            }
        };

        await SendAsync(notification, ct);

        participant.TrustedFriend.IsNotified = true;
        await participantRepo.UpdateAsync(participant, ct);
    }

    private Task<bool> TrySendPushAsync(NotificationDto notification, CancellationToken ct)
    {
        // TODO: Integrate FCM (Firebase Cloud Messaging) for Android / APNs for iOS
        logger.LogInformation("Sending push notification to device token ending in ...{Token}",
            notification.RecipientDeviceToken?[^4..]);
        return Task.FromResult(true);
    }

    private Task<bool> TrySendWebPushAsync(NotificationDto notification, CancellationToken ct)
    {
        // TODO: Implement Web Push using VAPID keys (WebPush library)
        // Falls back to email if Web Push subscription is expired or iOS Safari
        logger.LogInformation("Attempting Web Push for {Email}", notification.RecipientEmail);
        return Task.FromResult(false); // Returns false to trigger email fallback until implemented
    }

    private Task SendEmailAsync(NotificationDto notification, CancellationToken ct)
    {
        // TODO: Integrate SendGrid (or equivalent transactional email provider)
        // TODO: Implementar proveedor de WhatsApp para futura monetización
        logger.LogInformation("Sending transactional email to {Email}: {Title}", notification.RecipientEmail, notification.Title);
        return Task.CompletedTask;
    }
}
