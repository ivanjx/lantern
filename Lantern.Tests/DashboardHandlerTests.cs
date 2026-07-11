using System.Text;
using Lantern.Configuration;
using Lantern.Dashboard;
using Lantern.Devices;
using Lantern.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.Tests;

public sealed class DashboardHandlerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lantern-{Guid.NewGuid():N}");

    [Fact]
    public async Task RenameAsync_TrimsNameAndRedirectsWithSuccessFeedback()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        await repository.UpsertObservationAsync(new("AA:BB:CC:DD:EE:FF", null, null, DateTimeOffset.UtcNow));
        var handler = new DashboardHandler(repository, new PollStatus());
        var context = CreateFormContext("friendlyName=++Kitchen+tablet++");

        var result = await handler.RenameAsync("AA:BB:CC:DD:EE:FF", context.Request, default);

        Assert.Equal("/?feedback=renamed", Assert.IsType<RedirectHttpResult>(result).Url);
        var device = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync("AA:BB:CC:DD:EE:FF")).Value;
        Assert.Equal("Kitchen tablet", device!.FriendlyName);
    }

    [Fact]
    public async Task RenameAsync_RejectsNamesLongerThanOneHundredCharacters()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var handler = new DashboardHandler(repository, new PollStatus());
        var context = CreateFormContext($"friendlyName={new string('a', 101)}");

        var result = await handler.RenameAsync("AA:BB:CC:DD:EE:FF", context.Request, default);

        Assert.Equal("/?feedback=name-too-long", Assert.IsType<RedirectHttpResult>(result).Url);
    }

    [Fact]
    public async Task TrustAsync_ReportsInvalidAndMissingDevices()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var handler = new DashboardHandler(repository, new PollStatus());

        var invalid = await handler.TrustAsync("invalid", default);
        var missing = await handler.TrustAsync("AA:BB:CC:DD:EE:FF", default);

        Assert.Equal("/?feedback=invalid-mac", Assert.IsType<RedirectHttpResult>(invalid).Url);
        Assert.Equal("/?feedback=device-missing", Assert.IsType<RedirectHttpResult>(missing).Url);
    }

    [Theory]
    [InlineData(DeviceStatus.Trusted, true)]
    [InlineData(DeviceStatus.Ignored, false)]
    public async Task UndoStatusAsync_ReturnsDeviceToUnknown(DeviceStatus initialStatus, bool untrust)
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        const string mac = "AA:BB:CC:DD:EE:FF";
        await repository.UpsertObservationAsync(new(mac, null, null, DateTimeOffset.UtcNow));
        await repository.SetStatusAsync(mac, initialStatus);
        var handler = new DashboardHandler(repository, new PollStatus());

        var result = untrust ?
            await handler.UntrustAsync(mac, default) :
            await handler.UnignoreAsync(mac, default);

        Assert.Equal(untrust ? "/?feedback=untrusted" : "/?feedback=unignored", Assert.IsType<RedirectHttpResult>(result).Url);
        var device = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync(mac)).Value;
        Assert.Equal(DeviceStatus.Unknown, device!.Status);
    }

    [Fact]
    public async Task DeleteAsync_DeletesOfflineUnknownDevice()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        const string mac = "AA:BB:CC:DD:EE:FF";
        var pollTime = DateTimeOffset.Parse("2026-07-11T10:01:00Z");
        await repository.UpsertObservationAsync(new(mac, null, null, pollTime.AddMinutes(-1)));
        var pollStatus = new PollStatus();
        pollStatus.RecordSuccess(pollTime);
        var handler = new DashboardHandler(repository, pollStatus);

        var result = await handler.DeleteAsync(mac, default);

        Assert.Equal("/?feedback=deleted", Assert.IsType<RedirectHttpResult>(result).Url);
        Assert.Null(Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync(mac)).Value);
    }

    [Fact]
    public async Task DeleteAsync_RejectsDeviceSeenInLatestPoll()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        const string mac = "AA:BB:CC:DD:EE:FF";
        var pollTime = DateTimeOffset.Parse("2026-07-11T10:01:00Z");
        await repository.UpsertObservationAsync(new(mac, null, null, pollTime));
        var pollStatus = new PollStatus();
        pollStatus.RecordSuccess(pollTime);
        var handler = new DashboardHandler(repository, pollStatus);

        var result = await handler.DeleteAsync(mac, default);

        Assert.Equal("/?feedback=device-not-deletable", Assert.IsType<RedirectHttpResult>(result).Url);
        Assert.NotNull(Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync(mac)).Value);
    }

    private static DefaultHttpContext CreateFormContext(string form)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(form));
        return context;
    }

    private DeviceRepository CreateRepository() => new(
        Options.Create(new DatabaseOptions { Path = Path.Combine(_directory, "lantern.db") }),
        NullLogger<DeviceRepository>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
