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
            var mikroTikResult = await mikroTikClient.GetActiveLeasesAsync(cancellationToken);

            if (mikroTikResult is not MikroTikLeasesResult leasesResult)
            {
                pollStatus.RecordFailure(GetMikroTikFailure(mikroTikResult));
                return;
            }

            var observedAtUtc = timeProvider.GetUtcNow();

            foreach (var lease in leasesResult.Leases)
            {
                var repositoryResult = await deviceRepository.UpsertObservationAsync(
                    new DeviceObservation(lease.MacAddress, lease.Address, lease.HostName, observedAtUtc),
                    cancellationToken);

                if (repositoryResult is not SuccessRepositoryResult)
                {
                    pollStatus.RecordFailure(GetRepositoryFailure(repositoryResult));
                    return;
                }
            }

            pollStatus.RecordSuccess(observedAtUtc);
            logger.LogDebug("MikroTik poll succeeded with {LeaseCount} active leases", leasesResult.Leases.Count);
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

    private static string GetMikroTikFailure(ServiceResult result) => result switch
    {
        MikroTikUnauthorizedErrorResult => "MikroTik authentication failed",
        MikroTikInvalidResponseErrorResult => "MikroTik returned an invalid response",
        ErrorServiceResult => "MikroTik is unavailable",
        _ => "MikroTik poll failed"
    };

    private static string GetRepositoryFailure(RepositoryResult result) => result switch
    {
        InvalidMacAddressRepositoryErrorResult => "An invalid device MAC address was rejected",
        ErrorRepositoryResult => "The device database is unavailable",
        _ => "The device observation could not be stored"
    };
}
