using Microsoft.AspNetCore.SignalR;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Shared.DTOs;

namespace RendezvousNET.Api.Hubs;

public class MeetingStatusHubClient(IHubContext<MeetingStatusHub> hubContext) : IMeetingStatusHub
{
    public Task BroadcastStatusUpdateAsync(ParticipantStatusUpdatedDto update) =>
        hubContext.Clients
            .Group($"meeting-{update.MeetingId}")
            .SendAsync("ParticipantStatusUpdated", update);

    public Task BroadcastBatchStatusUpdatesAsync(Guid meetingId, IEnumerable<ParticipantStatusUpdatedDto> updates) =>
        hubContext.Clients
            .Group($"meeting-{meetingId}")
            .SendAsync("BatchStatusUpdated", updates);
}
