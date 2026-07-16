using Lantern.MikroTik;
using Lantern.Monitoring;

namespace Lantern.Devices;

internal sealed class DeviceDetectionService(
    IMikroTikClient _mikroTikClient,
    DeviceRepository _repository,
    PollStatus _pollStatus,
    TimeProvider _timeProvider,
    ILogger<DeviceDetectionService> _logger)
{
    public async Task<ServiceResult> PollAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var mikroTikResult = await _mikroTikClient.GetActiveLeasesAsync(cancellationToken);

            if (mikroTikResult is CanceledServiceResult)
            {
                return mikroTikResult;
            }

            if (mikroTikResult is not MikroTikLeasesResult leasesResult)
            {
                _pollStatus.RecordFailure(GetMikroTikFailure(mikroTikResult));
                return new ErrorServiceResult();
            }

            var observedAtUtc = _timeProvider.GetUtcNow();
            var observations = leasesResult.Leases
                .Select(lease => new DeviceObservation(lease.MacAddress, lease.Address, lease.HostName, observedAtUtc))
                .ToArray();
            var detectionResult = await ProcessAsync(observations, cancellationToken);

            if (detectionResult is CanceledServiceResult)
            {
                return detectionResult;
            }

            if (detectionResult is not DeviceDetectionSuccessResult)
            {
                _pollStatus.RecordFailure("The device observations could not be processed");
                return new ErrorServiceResult();
            }

            _pollStatus.RecordSuccess(observedAtUtc);
            _logger.LogDebug("MikroTik poll succeeded with {LeaseCount} active leases", leasesResult.Leases.Count);
            return detectionResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledServiceResult();
        }
        catch (Exception exception)
        {
            _pollStatus.RecordFailure(exception.Message);
            _logger.LogError(exception, "MikroTik poll failed");
            return new ErrorServiceResult();
        }
    }

    private async Task<ServiceResult> ProcessAsync(
        IReadOnlyList<DeviceObservation> observations,
        CancellationToken cancellationToken = default)
    {
        var stateResult = await _repository.IsInitialScanCompletedAsync(cancellationToken);

        if (stateResult is not RepositoryResult<bool> state)
        {
            return FromRepositoryFailure(stateResult);
        }

        var notifications = new List<Device>();

        foreach (var observation in observations)
        {
            var existingResult = await _repository.GetAsync(observation.MacAddress, cancellationToken);

            if (existingResult is not RepositoryResult<Device?> existing)
            {
                return FromRepositoryFailure(existingResult);
            }

            var upsertResult = await _repository.UpsertObservationAsync(observation, cancellationToken);

            if (upsertResult is not SuccessRepositoryResult)
            {
                return FromRepositoryFailure(upsertResult);
            }

            if (state.Value && existing.Value is null)
            {
                var pendingResult = await _repository.MarkNotificationPendingAsync(
                    observation.MacAddress,
                    cancellationToken);

                if (pendingResult is not SuccessRepositoryResult)
                {
                    return FromRepositoryFailure(pendingResult);
                }

                _logger.LogInformation("New unknown device detected: {MacAddress}", observation.MacAddress);
            }

            if (state.Value)
            {
                var pendingResult = await _repository.IsNotificationPendingAsync(observation.MacAddress, cancellationToken);

                if (pendingResult is not RepositoryResult<bool> pending)
                {
                    return FromRepositoryFailure(pendingResult);
                }

                var deviceResult = await _repository.GetAsync(observation.MacAddress, cancellationToken);

                if (deviceResult is not RepositoryResult<Device?> deviceRepositoryResult)
                {
                    return FromRepositoryFailure(deviceResult);
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
            var completionResult = await _repository.MarkInitialScanCompletedAsync(cancellationToken);

            if (completionResult is not SuccessRepositoryResult)
            {
                return FromRepositoryFailure(completionResult);
            }

            _logger.LogInformation("Initial baseline scan completed with {DeviceCount} devices", observations.Count);
        }

        return new DeviceDetectionSuccessResult(notifications, !state.Value);
    }

    private static ServiceResult FromRepositoryFailure(RepositoryResult result) =>
        result is CanceledRepositoryResult ?
            new CanceledServiceResult() :
            new ErrorServiceResult();

    private static string GetMikroTikFailure(ServiceResult result) => result switch
    {
        MikroTikUnauthorizedErrorResult => "MikroTik authentication failed",
        MikroTikInvalidResponseErrorResult => "MikroTik returned an invalid response",
        CanceledServiceResult => "MikroTik poll was canceled",
        ErrorServiceResult => "MikroTik is unavailable",
        _ => "MikroTik poll failed"
    };
}
