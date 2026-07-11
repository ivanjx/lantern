namespace Lantern.Devices;

internal sealed record DeviceRegistry(
    IReadOnlyList<Device> Devices,
    int UnknownCount,
    int TrustedCount);
