using RendezvousNET.Shared.DTOs;

namespace RendezvousNET.Application.Interfaces;

/// <summary>
/// SignalR hub abstraction for real-time participant status updates.
/// To avoid N individual broadcasts causing UI thrash, updates are batched
/// via a short debounce window before broadcasting.
/// </summary>
public interface IMeetingStatusHub
{
    Task BroadcastStatusUpdateAsync(ParticipantStatusUpdatedDto update);
    Task BroadcastBatchStatusUpdatesAsync(Guid meetingId, IEnumerable<ParticipantStatusUpdatedDto> updates);
}
