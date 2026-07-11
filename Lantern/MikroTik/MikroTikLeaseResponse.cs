using System.Text.Json.Serialization;

namespace Lantern.MikroTik;

internal sealed class MikroTikLeaseResponse
{
    [JsonPropertyName("mac-address")]
    public string? MacAddress { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("host-name")]
    public string? HostName { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("dynamic")]
    public string? Dynamic { get; init; }

    [JsonPropertyName("disabled")]
    public string? Disabled { get; init; }
}
