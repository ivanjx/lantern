using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Lantern.Configuration;
using Lantern.Devices;

namespace Lantern.Tests;

public sealed class DeviceRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lantern-{Guid.NewGuid():N}");

    [Fact]
    public async Task UpsertObservation_InsertsUnknownDeviceAndUpdatesObservation()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var firstSeen = DateTimeOffset.Parse("2026-07-11T10:00:00Z");

        await repository.UpsertObservationAsync(new("aa-bb-cc-dd-ee-ff", "192.168.1.2", "phone", firstSeen));
        await repository.UpsertObservationAsync(new("AA:BB:CC:DD:EE:FF", "192.168.1.3", "phone-new", firstSeen.AddMinutes(1)));

        var device = await repository.GetAsync("aabbccddeeff");
        Assert.NotNull(device);
        Assert.Equal(DeviceStatus.Unknown, device.Status);
        Assert.Equal(firstSeen, device.FirstSeenUtc);
        Assert.Equal(firstSeen.AddMinutes(1), device.LastSeenUtc);
        Assert.Equal("192.168.1.3", device.LastIpAddress);
        Assert.Equal("phone-new", device.LastHostname);
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
