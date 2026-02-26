using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Shared.DTOs.Participants;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Api.Hubs;

/// <summary>
/// SignalR hub for real-time meeting-dashboard updates.
///
/// Performance note: Rather than broadcasting individual per-participant messages,
/// the hub uses meeting-scoped groups so only clients viewing that specific meeting
/// receive updates. This keeps the fan-out bounded (O(participants in meeting)) and
/// avoids broadcasting to all connected clients even under high load.
///
/// Clients join/leave their meeting group by calling
/// <see cref="JoinMeetingGroup"/> / <see cref="LeaveMeetingGroup"/>.
/// Status updates are sent via <see cref="BroadcastStatusUpdateAsync"/>.
/// </summary>
[Authorize]
public sealed class MeetingHub : Hub
{
    private const string StatusUpdatedMethod = "ParticipantStatusUpdated";
    private const string MeetingGroupPrefix = "meeting_";

    /// <summary>
    /// Called by clients when they open the meeting dashboard.
    /// Joins the client to the meeting-scoped SignalR group.
    /// </summary>
    public Task JoinMeetingGroup(Guid meetingId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(meetingId));

    /// <summary>
    /// Called by clients when they leave the meeting dashboard.
    /// </summary>
    public Task LeaveMeetingGroup(Guid meetingId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(meetingId));

    /// <summary>
    /// Broadcasts a participant status change to every client in the meeting group.
    /// Called server-side via <see cref="IHubContext{MeetingHub}"/>.
    /// </summary>
    public static Task BroadcastStatusUpdateAsync(
        IHubContext<MeetingHub> hubContext,
        ParticipantStatusUpdatedDto payload,
        CancellationToken cancellationToken = default)
    {
        return hubContext.Clients
            .Group(GroupName(payload.MeetingId))
            .SendAsync(StatusUpdatedMethod, payload, cancellationToken);
    }

    private static string GroupName(Guid meetingId) =>
        $"{MeetingGroupPrefix}{meetingId:N}";
}
