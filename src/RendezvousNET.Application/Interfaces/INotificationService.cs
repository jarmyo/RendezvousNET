using RendezvousNET.Shared.DTOs;

namespace RendezvousNET.Application.Interfaces;

/// <summary>
/// Abstraction for sending notifications. Implementation uses strategy pattern:
/// Push (FCM/APNs) for installed app, Web Push with Email fallback for web-only users.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a notification using the best available channel with fallback strategy.
    /// App installed → Push. Web only → WebPush, fallback to Email if WebPush fails (crucial for iOS Safari).
    /// </summary>
    Task SendAsync(NotificationDto notification, CancellationToken ct = default);

    /// <summary>
    /// Notifies trusted friend when their participant enters Emergency status.
    /// Reveals host location and meeting details to trusted friend.
    /// </summary>
    Task NotifyTrustedFriendEmergencyAsync(Guid participantId, CancellationToken ct = default);
}
