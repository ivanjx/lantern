using Lantern.Devices;
using Lantern.Telegram;

namespace Lantern.Monitoring;

internal sealed class DeviceMonitoringService(
    DeviceDetectionService detectionService,
    TelegramNotificationService notificationService)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (await detectionService.PollAsync(cancellationToken) is not DeviceDetectionSuccessResult detection)
        {
            return;
        }

        foreach (var device in detection.DevicesRequiringNotification)
        {
            await notificationService.NotifyAsync(device, cancellationToken);
        }
    }
}
