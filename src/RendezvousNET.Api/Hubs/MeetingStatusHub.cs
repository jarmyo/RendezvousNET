using Microsoft.AspNetCore.SignalR;

namespace RendezvousNET.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time meeting participant status updates.
/// 
/// Performance note: To prevent UI thrash when multiple participants update simultaneously,
/// the hub supports both individual and batched broadcasts. Clients join a meeting-specific
/// group so only relevant participants receive updates (not all connected clients).
/// 
/// Group naming: "meeting-{meetingId}"
/// </summary>
public class MeetingStatusHub : Hub
{
    public async Task JoinMeetingGroup(string meetingId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"meeting-{meetingId}");
    }

    public async Task LeaveMeetingGroup(string meetingId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"meeting-{meetingId}");
    }
}
