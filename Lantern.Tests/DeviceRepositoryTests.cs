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

    [Fact]
    public async Task GetAsync_ReturnsCanceledWhenCancellationIsRequested()
    {
        var repository = CreateRepository();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var result = await repository.GetAsync(
            "AA:BB:CC:DD:EE:FF",
            cancellationTokenSource.Token);

        Assert.IsType<CanceledRepositoryResult>(result);
    }

    [Fact]
    public async Task GetRegistryAsync_SortsUnknownFirstThenMostRecentlySeenAndCountsStatuses()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.Parse("2026-07-11T10:00:00Z");
        await repository.UpsertObservationAsync(new("00:00:00:00:00:01", null, "old-unknown", now));
        await repository.UpsertObservationAsync(new("00:00:00:00:00:02", null, "trusted", now.AddMinutes(2)));
        await repository.UpsertObservationAsync(new("00:00:00:00:00:03", null, "new-unknown", now.AddMinutes(1)));
        await repository.SetStatusAsync("00:00:00:00:00:02", DeviceStatus.Trusted);

        var result = await repository.GetRegistryAsync();

        var registry = Assert.IsType<RepositoryResult<DeviceRegistry>>(result).Value;
        Assert.Equal(2, registry.UnknownCount);
        Assert.Equal(1, registry.TrustedCount);
        Assert.Equal(
            ["00:00:00:00:00:03", "00:00:00:00:00:01", "00:00:00:00:00:02"],
            registry.Devices.Select(device => device.MacAddress));
    }

    [Fact]
    public async Task StatusAndRenameMutations_UpdateExistingDeviceAndReportMissingDevice()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        await repository.UpsertObservationAsync(new("AA:BB:CC:DD:EE:FF", null, "phone", DateTimeOffset.UtcNow));

        var statusResult = await repository.SetStatusAsync("aabbccddeeff", DeviceStatus.Ignored);
        var renameResult = await repository.RenameAsync("AA-BB-CC-DD-EE-FF", "Guest phone");
        var missingResult = await repository.RenameAsync("00:00:00:00:00:00", "Missing");

        Assert.IsType<SuccessRepositoryResult>(statusResult);
        Assert.IsType<SuccessRepositoryResult>(renameResult);
        Assert.IsType<DeviceNotFoundRepositoryErrorResult>(missingResult);
        var device = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync("AA:BB:CC:DD:EE:FF")).Value;
        Assert.Equal(DeviceStatus.Ignored, device!.Status);
        Assert.Equal("Guest phone", device.FriendlyName);

        var resetResult = await repository.SetStatusAsync("AA:BB:CC:DD:EE:FF", DeviceStatus.Unknown);

        Assert.IsType<SuccessRepositoryResult>(resetResult);
        device = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync("AA:BB:CC:DD:EE:FF")).Value;
        Assert.Equal(DeviceStatus.Unknown, device!.Status);
    }

    [Fact]
    public async Task RenameAsync_ClearsFriendlyName()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        await repository.UpsertObservationAsync(new("AA:BB:CC:DD:EE:FF", null, null, DateTimeOffset.UtcNow));
        await repository.RenameAsync("AA:BB:CC:DD:EE:FF", "Phone");

        await repository.RenameAsync("AA:BB:CC:DD:EE:FF", null);

        var device = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync("AA:BB:CC:DD:EE:FF")).Value;
        Assert.Null(device!.FriendlyName);
    }

    [Fact]
    public async Task DeleteUnknownOfflineAsync_DeletesOnlyUnknownDevicesNotSeenInLatestPoll()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var pollTime = DateTimeOffset.Parse("2026-07-11T10:01:00Z");
        await repository.UpsertObservationAsync(new("00:00:00:00:00:01", null, "offline", pollTime.AddMinutes(-1)));
        await repository.UpsertObservationAsync(new("00:00:00:00:00:02", null, "online", pollTime));
        await repository.UpsertObservationAsync(new("00:00:00:00:00:03", null, "trusted", pollTime.AddMinutes(-1)));
        await repository.SetStatusAsync("00:00:00:00:00:03", DeviceStatus.Trusted);

        var offlineResult = await repository.DeleteUnknownOfflineAsync("00:00:00:00:00:01", pollTime);
        var onlineResult = await repository.DeleteUnknownOfflineAsync("00:00:00:00:00:02", pollTime);
        var trustedResult = await repository.DeleteUnknownOfflineAsync("00:00:00:00:00:03", pollTime);

        Assert.IsType<SuccessRepositoryResult>(offlineResult);
        Assert.IsType<DeviceNotDeletableRepositoryErrorResult>(onlineResult);
        Assert.IsType<DeviceNotDeletableRepositoryErrorResult>(trustedResult);
        Assert.Null(Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync("00:00:00:00:00:01")).Value);
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
