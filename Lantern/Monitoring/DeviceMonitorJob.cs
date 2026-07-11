using Lantern.Configuration;
using Microsoft.Extensions.Options;

namespace Lantern.Monitoring;

internal sealed class DeviceMonitorJob(
    DeviceMonitoringService monitoringService,
    IOptions<LanternOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly TimeSpan pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await monitoringService.RunAsync(stoppingToken);
        using var timer = new PeriodicTimer(pollInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await monitoringService.RunAsync(stoppingToken);
        }
    }
}
