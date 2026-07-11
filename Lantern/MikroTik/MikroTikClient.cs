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
    private readonly ILogger<MikroTikClient> logger;

    public MikroTikClient(
        HttpClient httpClient,
        IOptions<MikroTikOptions> options,
        ILogger<MikroTikClient> logger)
    {
        var settings = options.Value;
        this.httpClient = httpClient;
        this.logger = logger;
        this.httpClient.BaseAddress = new Uri(EnsureTrailingSlash(settings.BaseUrl), UriKind.Absolute);
        this.httpClient.Timeout = TimeSpan.FromSeconds(10);

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
        this.httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ServiceResult> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(LeasePath, cancellationToken);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                logger.LogError("MikroTik request was rejected with HTTP {StatusCode}", (int)response.StatusCode);
                return new MikroTikUnauthorizedErrorResult();
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("MikroTik request failed with HTTP {StatusCode}", (int)response.StatusCode);
                return new ErrorServiceResult();
            }

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

            return new MikroTikLeasesResult(activeLeases);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "MikroTik request timed out");
            return new ErrorServiceResult();
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "MikroTik request failed");
            return new ErrorServiceResult();
        }
        catch (System.Text.Json.JsonException exception)
        {
            logger.LogError(exception, "MikroTik returned an invalid response");
            return new MikroTikInvalidResponseErrorResult();
        }
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
