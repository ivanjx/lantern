using Lantern.Configuration;
using Microsoft.Extensions.Options;

namespace Lantern.Monitoring;

internal sealed class DeviceMonitorJob(
    DeviceMonitoringService _monitoringService,
    IOptions<LanternOptions> _options,
    TimeProvider _timeProvider) : BackgroundService
{
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _monitoringService.RunAsync(stoppingToken);
        using var timer = new PeriodicTimer(_pollInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _monitoringService.RunAsync(stoppingToken);
        }
    }
}
