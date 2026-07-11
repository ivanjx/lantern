namespace Lantern.Devices;

internal sealed record DeviceDetectionSuccessResult(
    IReadOnlyList<Device> DevicesRequiringNotification,
    bool BaselineCompleted) : SuccessServiceResult;
