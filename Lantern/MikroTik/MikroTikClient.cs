using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Lantern.Configuration;
using Lantern.Devices;
using Lantern.Json;
using Microsoft.Extensions.Options;

namespace Lantern.MikroTik;

internal sealed class MikroTikClient
{
    private const string LeasePath = "rest/ip/dhcp-server/lease";
    private readonly HttpClient httpClient;

    public MikroTikClient(HttpClient httpClient, IOptions<MikroTikOptions> options)
    {
        var settings = options.Value;
        this.httpClient = httpClient;
        this.httpClient.BaseAddress = new Uri(EnsureTrailingSlash(settings.BaseUrl), UriKind.Absolute);
        this.httpClient.Timeout = TimeSpan.FromSeconds(10);

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
        this.httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MikroTikLease>> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(LeasePath, cancellationToken);
        response.EnsureSuccessStatusCode();

        var leases = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.Default.MikroTikLeaseResponseArray,
            cancellationToken) ?? [];
        var activeLeases = new List<MikroTikLease>(leases.Length);

        foreach (var lease in leases)
        {
            if (!IsActive(lease) || !MacAddress.TryNormalize(lease.MacAddress, out var macAddress))
            {
                continue;
            }

            activeLeases.Add(new MikroTikLease(
                macAddress,
                NullIfWhiteSpace(lease.Address),
                NullIfWhiteSpace(lease.HostName),
                NullIfWhiteSpace(lease.Status),
                IsTrue(lease.Dynamic),
                IsTrue(lease.Disabled)));
        }

        return activeLeases;
    }

    private static bool IsActive(MikroTikLeaseResponse lease) =>
        !IsTrue(lease.Disabled) &&
        (string.Equals(lease.Status, "bound", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(lease.Status, "active", StringComparison.OrdinalIgnoreCase));

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "yes";

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith('/') ? value : $"{value}/";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
