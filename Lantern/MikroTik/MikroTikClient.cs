using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Lantern.Configuration;
using Lantern.Devices;
using Microsoft.Extensions.Options;

namespace Lantern.MikroTik;

internal interface IMikroTikClient
{
    Task<ServiceResult> GetActiveLeasesAsync(CancellationToken cancellationToken = default);
}

internal sealed class MikroTikClient : IMikroTikClient
{
    private const string LeasePath = "rest/ip/dhcp-server/lease";
    private readonly HttpClient _httpClient;
    private readonly ILogger<MikroTikClient> _logger;

    public MikroTikClient(
        HttpClient httpClient,
        IOptions<MikroTikOptions> options,
        ILogger<MikroTikClient> logger)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(settings.BaseUrl), UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ServiceResult> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LeasePath, cancellationToken);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogError("MikroTik request was rejected with HTTP {StatusCode}", (int)response.StatusCode);
                return new MikroTikUnauthorizedErrorResult();
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("MikroTik request failed with HTTP {StatusCode}", (int)response.StatusCode);
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
            _logger.LogError(exception, "MikroTik request timed out");
            return new ErrorServiceResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledServiceResult();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "MikroTik request failed");
            return new ErrorServiceResult();
        }
        catch (System.Text.Json.JsonException exception)
        {
            _logger.LogError(exception, "MikroTik returned an invalid response");
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
