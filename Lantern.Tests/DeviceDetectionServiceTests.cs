using Lantern.Configuration;
using Lantern.Devices;
using Lantern.MikroTik;
using Lantern.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.Tests;

public sealed class DeviceDetectionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lantern-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstScan_BaselinesDevicesWithoutNotification()
    {
        var (repository, detector, mikroTikClient) = await CreateAsync();
        mikroTikClient.Leases = [Lease("AA:BB:CC:DD:EE:01")];

        var result = await detector.PollAsync();

        var detection = Assert.IsType<DeviceDetectionSuccessResult>(result);
        Assert.True(detection.BaselineCompleted);
        Assert.Empty(detection.DevicesRequiringNotification);
        Assert.True(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsInitialScanCompletedAsync()).Value);
    }

    [Fact]
    public async Task NewDeviceAfterBaseline_RemainsPendingUntilDeliveryThenIsSuppressed()
    {
        var (repository, detector, mikroTikClient) = await CreateAsync();
        await detector.PollAsync();
        const string macAddress = "AA:BB:CC:DD:EE:02";
        mikroTikClient.Leases = [Lease(macAddress)];

        var first = Assert.IsType<DeviceDetectionSuccessResult>(
            await detector.PollAsync());
        var retry = Assert.IsType<DeviceDetectionSuccessResult>(
            await detector.PollAsync());

        Assert.Single(first.DevicesRequiringNotification);
        Assert.Single(retry.DevicesRequiringNotification);

        var deliveredAt = DateTimeOffset.Parse("2026-07-11T10:02:00Z");
        Assert.IsType<SuccessRepositoryResult>(await repository.MarkNotificationDeliveredAsync(
            macAddress, deliveredAt));
        var afterDelivery = Assert.IsType<DeviceDetectionSuccessResult>(
            await detector.PollAsync());

        Assert.Empty(afterDelivery.DevicesRequiringNotification);
        var device = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync(macAddress)).Value;
        Assert.Equal(deliveredAt, device!.LastNotificationUtc);
        Assert.False(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsNotificationPendingAsync(macAddress)).Value);
    }

    [Fact]
    public async Task BaselineDevice_DoesNotBecomePendingOnLaterPoll()
    {
        var (_, detector, mikroTikClient) = await CreateAsync();
        mikroTikClient.Leases = [Lease("AA:BB:CC:DD:EE:03")];
        await detector.PollAsync();

        var result = Assert.IsType<DeviceDetectionSuccessResult>(
            await detector.PollAsync());

        Assert.Empty(result.DevicesRequiringNotification);
    }

    private async Task<(DeviceRepository Repository, DeviceDetectionService Detector, StubMikroTikClient MikroTikClient)> CreateAsync()
    {
        var repository = new DeviceRepository(
            Options.Create(new DatabaseOptions { Path = Path.Combine(_directory, "lantern.db") }),
            NullLogger<DeviceRepository>.Instance);
        await repository.InitializeAsync();
        var mikroTikClient = new StubMikroTikClient();
        return (repository, new DeviceDetectionService(
            mikroTikClient,
            repository,
            new PollStatus(),
            TimeProvider.System,
            NullLogger<DeviceDetectionService>.Instance),
            mikroTikClient);
    }

    private static MikroTikLease Lease(string macAddress) => new(
        macAddress,
        "192.168.1.10",
        "device",
        "bound",
        true,
        false);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StubMikroTikClient : IMikroTikClient
    {
        public IReadOnlyList<MikroTikLease> Leases { get; set; } = [];

        public Task<ServiceResult> GetActiveLeasesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ServiceResult>(new MikroTikLeasesResult(Leases));
    }
}
