using Microsoft.AspNetCore.SignalR.Client;
using RendezvousNET.Shared.DTOs;
using RendezvousNET.Shared.Enums;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RendezvousNET.Mobile.ViewModels;

/// <summary>
/// ViewModel for the Meeting Dashboard page.
/// Manages real-time participant status updates via SignalR.
///
/// Performance note: Instead of sending one SignalR message per participant update,
/// we support batch updates via "BatchStatusUpdated" to reduce UI re-render cycles
/// when multiple statuses change simultaneously.
/// </summary>
public class MeetingDashboardViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private MeetingDto? _meeting;
    private string _timerDisplay = "--:--";
    private string _timerChipColor = "#FFFFFF20";
    private bool _isRefreshing;
    private HubConnection? _hubConnection;
    private System.Threading.Timer? _timer;

    public ObservableCollection<MeetingParticipantDto> Participants { get; } = [];

    public MeetingDto? Meeting
    {
        get => _meeting;
        set { _meeting = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScheduledAtDisplay)); OnPropertyChanged(nameof(ParticipantCountLabel)); }
    }

    public string TimerDisplay
    {
        get => _timerDisplay;
        set { _timerDisplay = value; OnPropertyChanged(); }
    }

    public string TimerChipColor
    {
        get => _timerChipColor;
        set { _timerChipColor = value; OnPropertyChanged(); }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { _isRefreshing = value; OnPropertyChanged(); }
    }

    public string ScheduledAtDisplay => Meeting?.ScheduledAt.ToLocalTime().ToString("ddd dd MMM, HH:mm") ?? string.Empty;

    public string ParticipantCountLabel => $"Invitados ({Participants.Count})";

    public ICommand RefreshCommand { get; }

    public MeetingDashboardViewModel()
    {
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
    }

    public async Task InitializeAsync(MeetingDto meeting, string hubUrl)
    {
        Meeting = meeting;
        Participants.Clear();
        foreach (var p in meeting.Participants)
            Participants.Add(p);

        await ConnectToSignalRAsync(hubUrl, meeting.Id.ToString());
        StartTimer();
    }

    private async Task ConnectToSignalRAsync(string hubUrl, string meetingId)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<ParticipantStatusUpdatedDto>("ParticipantStatusUpdated", OnStatusUpdated);

        _hubConnection.On<IEnumerable<ParticipantStatusUpdatedDto>>("BatchStatusUpdated", updates =>
        {
            foreach (var update in updates)
                OnStatusUpdated(update);
        });

        await _hubConnection.StartAsync();
        await _hubConnection.SendAsync("JoinMeetingGroup", meetingId);
    }

    private void OnStatusUpdated(ParticipantStatusUpdatedDto update)
    {
        var participant = Participants.FirstOrDefault(p => p.Id == update.ParticipantId);
        if (participant is null) return;
        participant.Status = update.Status;
        participant.CurrentLatitude = update.CurrentLatitude;
        participant.CurrentLongitude = update.CurrentLongitude;
        OnPropertyChanged(nameof(Participants));
    }

    private void StartTimer()
    {
        _timer = new System.Threading.Timer(_ =>
        {
            if (Meeting is null) return;
            var diff = Meeting.ScheduledAt - DateTimeOffset.UtcNow;
            var late = diff.TotalSeconds < 0;
            TimerDisplay = late
                ? $"+{Math.Abs((int)diff.TotalMinutes):D2}:{Math.Abs(diff.Seconds):D2}"
                : $"{(int)diff.TotalMinutes:D2}:{diff.Seconds:D2}";
            TimerChipColor = late ? "#EF444480" : "#FFFFFF20";
        }, null, 0, 1000);
    }

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        // TODO: re-fetch meeting data from API
        await Task.Delay(500);
        IsRefreshing = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}
