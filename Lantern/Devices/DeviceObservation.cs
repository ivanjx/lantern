namespace Lantern.Devices;

internal sealed record DeviceObservation(
    string MacAddress,
    string? IpAddress,
    string? Hostname,
    DateTimeOffset ObservedAtUtc);
