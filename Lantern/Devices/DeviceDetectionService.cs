using Lantern.MikroTik;
using Lantern.Monitoring;

namespace Lantern.Devices;

internal sealed class DeviceDetectionService(
    IMikroTikClient mikroTikClient,
    DeviceRepository repository,
    PollStatus pollStatus,
    TimeProvider timeProvider,
    ILogger<DeviceDetectionService> logger)
{
    public async Task<ServiceResult> PollAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var mikroTikResult = await mikroTikClient.GetActiveLeasesAsync(cancellationToken);

            if (mikroTikResult is not MikroTikLeasesResult leasesResult)
            {
                pollStatus.RecordFailure(GetMikroTikFailure(mikroTikResult));
                return new ErrorServiceResult();
            }

            var observedAtUtc = timeProvider.GetUtcNow();
            var observations = leasesResult.Leases
                .Select(lease => new DeviceObservation(lease.MacAddress, lease.Address, lease.HostName, observedAtUtc))
                .ToArray();
            var detectionResult = await ProcessAsync(observations, cancellationToken);

            if (detectionResult is not DeviceDetectionSuccessResult)
            {
                pollStatus.RecordFailure("The device observations could not be processed");
                return new ErrorServiceResult();
            }

            pollStatus.RecordSuccess(observedAtUtc);
            logger.LogDebug("MikroTik poll succeeded with {LeaseCount} active leases", leasesResult.Leases.Count);
            return detectionResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            pollStatus.RecordFailure(exception.Message);
            logger.LogError(exception, "MikroTik poll failed");
            return new ErrorServiceResult();
        }
    }

    private async Task<ServiceResult> ProcessAsync(
        IReadOnlyList<DeviceObservation> observations,
        CancellationToken cancellationToken = default)
    {
        var stateResult = await repository.IsInitialScanCompletedAsync(cancellationToken);

        if (stateResult is not RepositoryResult<bool> state)
        {
            return new ErrorServiceResult();
        }

        var notifications = new List<Device>();

        foreach (var observation in observations)
        {
            var existingResult = await repository.GetAsync(observation.MacAddress, cancellationToken);

            if (existingResult is not RepositoryResult<Device?> existing)
            {
                return new ErrorServiceResult();
            }

            var upsertResult = await repository.UpsertObservationAsync(observation, cancellationToken);

            if (upsertResult is not SuccessRepositoryResult)
            {
                return new ErrorServiceResult();
            }

            if (state.Value && existing.Value is null)
            {
                if (await repository.MarkNotificationPendingAsync(observation.MacAddress, cancellationToken)
                    is not SuccessRepositoryResult)
                {
                    return new ErrorServiceResult();
                }

                logger.LogInformation("New unknown device detected: {MacAddress}", observation.MacAddress);
            }

            if (state.Value)
            {
                var pendingResult = await repository.IsNotificationPendingAsync(observation.MacAddress, cancellationToken);

                if (pendingResult is not RepositoryResult<bool> pending)
                {
                    return new ErrorServiceResult();
                }

                var deviceResult = await repository.GetAsync(observation.MacAddress, cancellationToken);

                if (deviceResult is not RepositoryResult<Device?> deviceRepositoryResult)
                {
                    return new ErrorServiceResult();
                }

                var device = deviceRepositoryResult.Value;

                if (device is null)
                {
                    return new ErrorServiceResult();
                }

                if (pending.Value && device.Status == DeviceStatus.Unknown && device.LastNotificationUtc is null)
                {
                    notifications.Add(device);
                }
            }
        }

        if (!state.Value)
        {
            var completionResult = await repository.MarkInitialScanCompletedAsync(cancellationToken);

            if (completionResult is not SuccessRepositoryResult)
            {
                return new ErrorServiceResult();
            }

            logger.LogInformation("Initial baseline scan completed with {DeviceCount} devices", observations.Count);
        }

        return new DeviceDetectionSuccessResult(notifications, !state.Value);
    }

    private static string GetMikroTikFailure(ServiceResult result) => result switch
    {
        MikroTikUnauthorizedErrorResult => "MikroTik authentication failed",
        MikroTikInvalidResponseErrorResult => "MikroTik returned an invalid response",
        ErrorServiceResult => "MikroTik is unavailable",
        _ => "MikroTik poll failed"
    };
}
