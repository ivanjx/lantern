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

        var insertResult = await repository.UpsertObservationAsync(
            new("aa-bb-cc-dd-ee-ff", "192.168.1.2", "phone", firstSeen));
        var updateResult = await repository.UpsertObservationAsync(
            new("AA:BB:CC:DD:EE:FF", "192.168.1.3", "phone-new", firstSeen.AddMinutes(1)));

        Assert.IsType<SuccessRepositoryResult>(insertResult);
        Assert.IsType<SuccessRepositoryResult>(updateResult);
        var getResult = await repository.GetAsync("aabbccddeeff");
        var device = Assert.IsType<RepositoryResult<Device?>>(getResult).Value;
        Assert.NotNull(device);
        Assert.Equal(DeviceStatus.Unknown, device.Status);
        Assert.Equal(firstSeen, device.FirstSeenUtc);
        Assert.Equal(firstSeen.AddMinutes(1), device.LastSeenUtc);
        Assert.Equal("192.168.1.3", device.LastIpAddress);
        Assert.Equal("phone-new", device.LastHostname);
    }

    [Fact]
    public async Task GetAsync_ReturnsInvalidMacAddressForInvalidInput()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync("not-a-mac");

        Assert.IsType<InvalidMacAddressRepositoryErrorResult>(result);
    }

    [Fact]
    public async Task UpsertObservation_ReturnsInvalidMacAddressForInvalidInput()
    {
        var repository = CreateRepository();
        var observation = new DeviceObservation("not-a-mac", null, null, DateTimeOffset.UtcNow);

        var result = await repository.UpsertObservationAsync(observation);

        Assert.IsType<InvalidMacAddressRepositoryErrorResult>(result);
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
