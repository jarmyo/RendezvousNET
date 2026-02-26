using RendezvousNET.Application.Interfaces;

namespace RendezvousNET.Api.BackgroundServices;

/// <summary>
/// Hosted service that periodically triggers check-in requests
/// for participants whose meeting time has arrived.
/// </summary>
public sealed class CheckInBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CheckInBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public CheckInBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CheckInBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IParticipantService>();
                await service.ProcessCheckInRequestsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing check-in requests.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
