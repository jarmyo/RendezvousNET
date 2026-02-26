using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.AspNetCore.SignalR.Client;
using RendezvousNET.Shared.DTOs.Meetings;
using RendezvousNET.Shared.DTOs.Participants;
using RendezvousNET.Shared.Enums;

namespace RendezvousNET.Mobile.ViewModels;

/// <summary>
/// ViewModel for the Meeting Dashboard page.
/// Exposes header data and the participant list for data-binding.
/// SignalR updates the participant collection in real-time.
/// </summary>
public sealed class MeetingDashboardViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private System.Threading.Timer? _countdownTimer;

    // ── Observable properties ───────────────────────────────────────────────

    private string _meetingTitle = string.Empty;
    public string MeetingTitle
    {
        get => _meetingTitle;
        set => SetProperty(ref _meetingTitle, value);
    }

    private string _hostDisplayName = string.Empty;
    public string HostDisplayName
    {
        get => _hostDisplayName;
        set => SetProperty(ref _hostDisplayName, value);
    }

    private string _location = string.Empty;
    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    private string _scheduledAtFormatted = string.Empty;
    public string ScheduledAtFormatted
    {
        get => _scheduledAtFormatted;
        set => SetProperty(ref _scheduledAtFormatted, value);
    }

    private DateTimeOffset _scheduledAt;
    public DateTimeOffset ScheduledAt
    {
        get => _scheduledAt;
        set
        {
            SetProperty(ref _scheduledAt, value);
            ScheduledAtFormatted = value.ToLocalTime().ToString("ddd, MMM d · h:mm tt");
        }
    }

    private string _countdownDisplay = "—";
    public string CountdownDisplay
    {
        get => _countdownDisplay;
        set => SetProperty(ref _countdownDisplay, value);
    }

    private Color _countdownColor = Colors.DodgerBlue;
    public Color CountdownColor
    {
        get => _countdownColor;
        set => SetProperty(ref _countdownColor, value);
    }

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    /// <summary>Participant list backing the CollectionView.</summary>
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];

    public ICommand RefreshCommand { get; }

    // ── Meeting data (set before navigating to this page) ──────────────────

    public Guid MeetingId { get; set; }
    public string? AccessToken { get; set; }
    public string HubUrl { get; set; } = "https://localhost:7000/hubs/meeting";

    // ── Constructor ─────────────────────────────────────────────────────────

    public MeetingDashboardViewModel()
    {
        RefreshCommand = new Command(async () => await RefreshParticipantsAsync());
    }

    // ── Initialization ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        StartCountdownTimer();
        await ConnectToHubAsync();
    }

    public async Task CleanupAsync()
    {
        _countdownTimer?.Dispose();
        if (_hubConnection is not null)
        {
            await _hubConnection.InvokeAsync("LeaveMeetingGroup", MeetingId);
            await _hubConnection.StopAsync();
        }
    }

    public void LoadMeeting(MeetingDto dto)
    {
        MeetingTitle = dto.Title;
        HostDisplayName = dto.Host.DisplayName;
        Location = dto.Location;
        ScheduledAt = dto.ScheduledAt;

        Participants.Clear();
        foreach (var p in dto.Participants)
            Participants.Add(new ParticipantItemViewModel(p));
    }

    // ── SignalR ─────────────────────────────────────────────────────────────

    private async Task ConnectToHubAsync()
    {
        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(HubUrl, options =>
                {
                    if (!string.IsNullOrEmpty(AccessToken))
                        options.AccessTokenProvider = () => Task.FromResult<string?>(AccessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ParticipantStatusUpdatedDto>("ParticipantStatusUpdated", OnStatusUpdated);

            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinMeetingGroup", MeetingId);
        }
        catch (Exception ex)
        {
            // Log error — UI stays functional with cached data
            System.Diagnostics.Debug.WriteLine($"SignalR connection failed: {ex.Message}");
        }
    }

    private void OnStatusUpdated(ParticipantStatusUpdatedDto payload)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existing = Participants.FirstOrDefault(p => p.Id == payload.ParticipantId);
            if (existing is not null)
                existing.UpdateStatus(payload);
        });
    }

    // ── Countdown Timer ─────────────────────────────────────────────────────

    private void StartCountdownTimer()
    {
        _countdownTimer = new System.Threading.Timer(_ =>
        {
            var remaining = _scheduledAt - DateTimeOffset.UtcNow;
            var display = remaining <= TimeSpan.Zero
                ? "In progress"
                : remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s"
                    : $"{remaining.Minutes:D2}m {remaining.Seconds:D2}s";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CountdownDisplay = display;
                CountdownColor = remaining <= TimeSpan.Zero ? Colors.LimeGreen : Colors.DodgerBlue;
            });
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private async Task RefreshParticipantsAsync()
    {
        IsRefreshing = true;
        // TODO: re-fetch meeting from API and reload participant list
        await Task.Delay(500);
        IsRefreshing = false;
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _countdownTimer?.Dispose();
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}

/// <summary>
/// Per-participant item for the CollectionView.
/// Exposes a <see cref="StatusColor"/> that maps <see cref="ParticipantStatus"/> to a MAUI Color.
/// </summary>
public sealed class ParticipantItemViewModel : INotifyPropertyChanged
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public bool IsAnonymousGuest { get; }
    public string ParticipantTypeLabel => IsAnonymousGuest ? "⚠️ Anonymous Guest" : string.Empty;

    private ParticipantStatus _status;
    public ParticipantStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            }
        }
    }

    /// <summary>
    /// Circle color reflecting real-time status:
    /// Gray=Pending, Green=Arrived, Yellow=Delayed, Red=Emergency.
    /// </summary>
    public Color StatusColor => Status switch
    {
        ParticipantStatus.Arrived   => Color.FromArgb("#22C55E"),
        ParticipantStatus.Delayed   => Color.FromArgb("#EAB308"),
        ParticipantStatus.Emergency => Color.FromArgb("#EF4444"),
        _                           => Color.FromArgb("#94A3B8")
    };

    public string StatusLabel => Status switch
    {
        ParticipantStatus.Arrived   => "✅ At the venue",
        ParticipantStatus.Delayed   => "🟡 Running late",
        ParticipantStatus.Emergency => "🚨 Emergency",
        _                           => "⬜ Pending"
    };

    public ParticipantItemViewModel(MeetingParticipantDto dto)
    {
        Id = dto.Id;
        DisplayName = dto.DisplayName;
        IsAnonymousGuest = dto.ParticipantType == ParticipantType.AnonymousGuest;
        _status = dto.Status;
    }

    public void UpdateStatus(ParticipantStatusUpdatedDto payload)
    {
        Status = payload.Status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
