namespace Lantern.Devices;

internal sealed record Device(
    string MacAddress,
    string? FriendlyName,
    DeviceStatus Status,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    string? LastIpAddress,
    string? LastHostname,
    DateTimeOffset? LastNotificationUtc);
