using Microsoft.AspNetCore.SignalR.Client;
using RendezvousNET.Shared.DTOs.Participants;

namespace RendezvousNET.Web.Services;

/// <summary>
/// Manages the SignalR connection for the Blazor client,
/// joining the appropriate meeting group and propagating status updates.
/// </summary>
public sealed class MeetingHubService : IAsyncDisposable
{
    private HubConnection? _connection;
    private Guid _currentMeetingId;

    public event Func<ParticipantStatusUpdatedDto, Task>? OnStatusUpdated;

    public async Task ConnectAsync(string hubUrl, Guid meetingId, string? accessToken = null)
    {
        _currentMeetingId = meetingId;

        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                if (!string.IsNullOrEmpty(accessToken))
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .WithAutomaticReconnect();

        _connection = builder.Build();

        _connection.On<ParticipantStatusUpdatedDto>("ParticipantStatusUpdated", async payload =>
        {
            if (OnStatusUpdated is not null)
                await OnStatusUpdated(payload);
        });

        await _connection.StartAsync();
        await _connection.InvokeAsync("JoinMeetingGroup", meetingId);
    }

    public async Task DisconnectAsync()
    {
        if (_connection is not null && _currentMeetingId != Guid.Empty)
        {
            await _connection.InvokeAsync("LeaveMeetingGroup", _currentMeetingId);
            await _connection.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
