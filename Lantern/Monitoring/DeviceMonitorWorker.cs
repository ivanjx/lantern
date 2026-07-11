using Lantern.Configuration;
using Lantern.Devices;
using Microsoft.Extensions.Options;

namespace Lantern.Monitoring;

internal sealed class DeviceMonitorWorker(
    DeviceDetectionService detectionService,
    IOptions<LanternOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly TimeSpan pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await detectionService.PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(pollInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await detectionService.PollAsync(stoppingToken);
        }
    }
}
