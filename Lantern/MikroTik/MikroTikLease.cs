namespace Lantern.MikroTik;

internal sealed record MikroTikLease(
    string MacAddress,
    string? Address,
    string? HostName,
    string? Status,
    bool Dynamic,
    bool Disabled);
