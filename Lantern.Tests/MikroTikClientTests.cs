using System.Net;
using System.Text;
using Lantern.Configuration;
using Lantern.MikroTik;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.Tests;

public sealed class MikroTikClientTests
{
    [Fact]
    public async Task GetActiveLeasesAsync_MapsAndFiltersRouterOsResponse()
    {
        const string json = """
            [
              {"mac-address":"aa-bb-cc-dd-ee-ff","address":"192.168.1.10","host-name":"phone","status":"bound","dynamic":"true","disabled":"false"},
              {"mac-address":"11:22:33:44:55:66","address":"192.168.1.11","status":"waiting","dynamic":"true","disabled":"false"},
              {"mac-address":"22:33:44:55:66:77","address":"192.168.1.12","status":"bound","dynamic":"true","disabled":"true"},
              {"mac-address":"not-a-mac","address":"192.168.1.13","status":"active","dynamic":"false","disabled":"false"}
            ]
            """;
        var handler = new StubHttpMessageHandler(json);
        var client = CreateClient(handler);

        var result = await client.GetActiveLeasesAsync(CancellationToken.None);

        var success = Assert.IsType<MikroTikLeasesResult>(result);
        var lease = Assert.Single(success.Leases);
        Assert.Equal("AA:BB:CC:DD:EE:FF", lease.MacAddress);
        Assert.Equal("192.168.1.10", lease.Address);
        Assert.Equal("phone", lease.HostName);
        Assert.True(lease.Dynamic);
        Assert.False(lease.Disabled);
        Assert.Equal(new Uri("https://router.example/rest/ip/dhcp-server/lease"), handler.RequestUri);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("reader:secret")), handler.AuthorizationParameter);
    }

    [Fact]
    public async Task GetActiveLeasesAsync_ReturnsUnavailableForNonSuccessResponse()
    {
        var handler = new StubHttpMessageHandler("{}", HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler);

        var result = await client.GetActiveLeasesAsync(CancellationToken.None);

        Assert.IsType<ErrorServiceResult>(result);
    }

    [Fact]
    public async Task GetActiveLeasesAsync_ReturnsUnauthorizedWhenCredentialsAreRejected()
    {
        var handler = new StubHttpMessageHandler("{}", HttpStatusCode.Unauthorized);
        var client = CreateClient(handler);

        var result = await client.GetActiveLeasesAsync(CancellationToken.None);

        Assert.IsType<MikroTikUnauthorizedErrorResult>(result);
    }

    [Fact]
    public async Task GetActiveLeasesAsync_ReturnsInvalidResponseForMalformedJson()
    {
        var handler = new StubHttpMessageHandler("not-json");
        var client = CreateClient(handler);

        var result = await client.GetActiveLeasesAsync(CancellationToken.None);

        Assert.IsType<MikroTikInvalidResponseErrorResult>(result);
    }

    private static MikroTikClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MikroTikOptions
        {
            BaseUrl = "https://router.example",
            Username = "reader",
            Password = "secret"
        });
        return new MikroTikClient(
            new HttpClient(handler),
            options,
            NullLogger<MikroTikClient>.Instance);
    }

    private sealed class StubHttpMessageHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
