using Lantern.Devices;
using Lantern.Telegram;

namespace Lantern.Monitoring;

internal sealed class DeviceMonitoringService(
    DeviceDetectionService _detectionService,
    TelegramNotificationService _notificationService)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (await _detectionService.PollAsync(cancellationToken) is not DeviceDetectionSuccessResult detection)
        {
            return;
        }

        foreach (var device in detection.DevicesRequiringNotification)
        {
            await _notificationService.NotifyAsync(device, cancellationToken);
        }
    }
}
