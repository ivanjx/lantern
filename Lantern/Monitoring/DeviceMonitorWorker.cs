using Lantern.Configuration;
using Lantern.Devices;
using Lantern.MikroTik;
using Microsoft.Extensions.Options;

namespace Lantern.Monitoring;

internal sealed class DeviceMonitorWorker(
    MikroTikClient mikroTikClient,
    DeviceRepository deviceRepository,
    PollStatus pollStatus,
    IOptions<LanternOptions> options,
    TimeProvider timeProvider,
    ILogger<DeviceMonitorWorker> logger) : BackgroundService
{
    private readonly TimeSpan pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(pollInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAsync(stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            var leases = await mikroTikClient.GetActiveLeasesAsync(cancellationToken);
            var observedAtUtc = timeProvider.GetUtcNow();

            foreach (var lease in leases)
            {
                await deviceRepository.UpsertObservationAsync(
                    new DeviceObservation(lease.MacAddress, lease.Address, lease.HostName, observedAtUtc),
                    cancellationToken);
            }

            pollStatus.RecordSuccess(observedAtUtc);
            logger.LogDebug("MikroTik poll succeeded with {LeaseCount} active leases", leases.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            pollStatus.RecordFailure(exception.Message);
            logger.LogError(exception, "MikroTik poll failed");
        }
    }
}
