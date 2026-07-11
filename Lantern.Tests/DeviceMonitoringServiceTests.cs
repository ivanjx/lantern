using Lantern.Configuration;
using Lantern.Devices;
using Lantern.MikroTik;
using Lantern.Monitoring;
using Lantern.Telegram;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.Tests;

public sealed class DeviceMonitoringServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"lantern-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstRun_BaselinesDevicesWithoutSendingNotification()
    {
        var (repository, service, mikroTikClient, telegramClient) = await CreateAsync();
        mikroTikClient.Leases = [Lease("AA:BB:CC:DD:EE:01")];

        await service.RunAsync();

        Assert.Empty(telegramClient.Messages);
        Assert.True(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsInitialScanCompletedAsync()).Value);
    }

    [Fact]
    public async Task NewDeviceAfterBaseline_SendsNotificationAndRecordsDelivery()
    {
        var (repository, service, mikroTikClient, telegramClient) = await CreateAsync();
        await service.RunAsync();
        const string macAddress = "AA:BB:CC:DD:EE:02";
        mikroTikClient.Leases = [Lease(macAddress)];

        await service.RunAsync();
        await service.RunAsync();

        var message = Assert.Single(telegramClient.Messages);
        Assert.Contains(macAddress, message);
        Assert.Contains("192.168.1.10", message);
        Assert.Contains("device", message);
        Assert.Contains("http://lantern.local", message);
        var stored = Assert.IsType<RepositoryResult<Device?>>(await repository.GetAsync(macAddress)).Value;
        Assert.Equal(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero), stored!.LastNotificationUtc);
        Assert.False(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsNotificationPendingAsync(macAddress)).Value);
    }

    [Fact]
    public async Task FailedNotification_IsRetriedOnNextRun()
    {
        var (repository, service, mikroTikClient, telegramClient) = await CreateAsync();
        await service.RunAsync();
        const string macAddress = "AA:BB:CC:DD:EE:03";
        mikroTikClient.Leases = [Lease(macAddress)];
        telegramClient.SendResult = new ErrorServiceResult();

        await service.RunAsync();

        Assert.True(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsNotificationPendingAsync(macAddress)).Value);
        telegramClient.SendResult = new SuccessServiceResult();

        await service.RunAsync();

        Assert.Equal(2, telegramClient.Messages.Count);
        Assert.False(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsNotificationPendingAsync(macAddress)).Value);
    }

    [Fact]
    public async Task BaselineDevice_DoesNotSendOnLaterRun()
    {
        var (_, service, mikroTikClient, telegramClient) = await CreateAsync();
        mikroTikClient.Leases = [Lease("AA:BB:CC:DD:EE:04")];

        await service.RunAsync();
        await service.RunAsync();

        Assert.Empty(telegramClient.Messages);
    }

    [Fact]
    public async Task DetectionFailure_DoesNotSendNotificationOrCompleteInitialScan()
    {
        var (repository, service, mikroTikClient, telegramClient) = await CreateAsync();
        mikroTikClient.GetLeasesResult = new ErrorServiceResult();

        await service.RunAsync();

        Assert.Empty(telegramClient.Messages);
        Assert.False(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsInitialScanCompletedAsync()).Value);
    }

    [Fact]
    public async Task MultipleNewDevices_SendsNotificationForEachDevice()
    {
        var (repository, service, mikroTikClient, telegramClient) = await CreateAsync();
        await service.RunAsync();
        const string firstMacAddress = "AA:BB:CC:DD:EE:05";
        const string secondMacAddress = "AA:BB:CC:DD:EE:06";
        mikroTikClient.Leases = [Lease(firstMacAddress), Lease(secondMacAddress)];

        await service.RunAsync();

        Assert.Equal(2, telegramClient.Messages.Count);
        Assert.Contains(telegramClient.Messages, message => message.Contains(firstMacAddress));
        Assert.Contains(telegramClient.Messages, message => message.Contains(secondMacAddress));
        Assert.False(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsNotificationPendingAsync(firstMacAddress)).Value);
        Assert.False(Assert.IsType<RepositoryResult<bool>>(
            await repository.IsNotificationPendingAsync(secondMacAddress)).Value);
    }

    [Fact]
    public async Task FailedNotification_DoesNotPreventAttemptingOtherDevices()
    {
        var (_, service, mikroTikClient, telegramClient) = await CreateAsync();
        await service.RunAsync();
        mikroTikClient.Leases = [Lease("AA:BB:CC:DD:EE:07"), Lease("AA:BB:CC:DD:EE:08")];
        telegramClient.SendResult = new ErrorServiceResult();

        await service.RunAsync();

        Assert.Equal(2, telegramClient.Messages.Count);
    }

    [Fact]
    public async Task RunAsync_PropagatesCancellationTokenToClients()
    {
        var (_, service, mikroTikClient, telegramClient) = await CreateAsync();
        await service.RunAsync();
        mikroTikClient.Leases = [Lease("AA:BB:CC:DD:EE:09")];
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.RunAsync(cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, mikroTikClient.CancellationToken);
        Assert.Equal(cancellationTokenSource.Token, telegramClient.CancellationToken);
    }

    private async Task<(DeviceRepository Repository, DeviceMonitoringService Service,
        StubMikroTikClient MikroTikClient, StubTelegramClient TelegramClient)> CreateAsync()
    {
        var repository = new DeviceRepository(
            Options.Create(new DatabaseOptions { Path = Path.Combine(directory, "lantern.db") }),
            NullLogger<DeviceRepository>.Instance);
        await repository.InitializeAsync();
        var mikroTikClient = new StubMikroTikClient();
        var detectionService = new DeviceDetectionService(
            mikroTikClient,
            repository,
            new PollStatus(),
            TimeProvider.System,
            NullLogger<DeviceDetectionService>.Instance);
        var telegramClient = new StubTelegramClient();
        var notificationService = new TelegramNotificationService(
            telegramClient,
            repository,
            Options.Create(new TelegramOptions { PublicBaseUrl = "http://lantern.local/" }),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<TelegramNotificationService>.Instance);
        return (repository, new DeviceMonitoringService(detectionService, notificationService),
            mikroTikClient, telegramClient);
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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubMikroTikClient : IMikroTikClient
    {
        public IReadOnlyList<MikroTikLease> Leases { get; set; } = [];
        public ServiceResult? GetLeasesResult { get; set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ServiceResult> GetActiveLeasesAsync(CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            return Task.FromResult(GetLeasesResult ?? new MikroTikLeasesResult(Leases));
        }
    }

    private sealed class StubTelegramClient : ITelegramClient
    {
        public ServiceResult SendResult { get; set; } = new SuccessServiceResult();
        public List<string> Messages { get; } = [];
        public CancellationToken CancellationToken { get; private set; }

        public Task<ServiceResult> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            Messages.Add(message);
            return Task.FromResult(SendResult);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
