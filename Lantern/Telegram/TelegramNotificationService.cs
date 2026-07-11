using Lantern.Configuration;
using Lantern.Devices;
using Microsoft.Extensions.Options;

namespace Lantern.Telegram;

internal sealed class TelegramNotificationService(
    ITelegramClient _client,
    DeviceRepository _repository,
    IOptions<TelegramOptions> _options,
    TimeProvider _timeProvider,
    ILogger<TelegramNotificationService> _logger)
{
    public async Task NotifyAsync(Device device, CancellationToken cancellationToken = default)
    {
        var result = await _client.SendMessageAsync(FormatMessage(device, _options.Value.PublicBaseUrl), cancellationToken);

        if (result is not SuccessServiceResult)
        {
            _logger.LogWarning("Telegram notification remains pending for device {MacAddress}", device.MacAddress);
            return;
        }

        var deliveredAtUtc = _timeProvider.GetUtcNow();
        if (await _repository.MarkNotificationDeliveredAsync(device.MacAddress, deliveredAtUtc, cancellationToken)
            is SuccessRepositoryResult)
        {
            _logger.LogInformation("Telegram notification delivered for device {MacAddress}", device.MacAddress);
        }
    }

    internal static string FormatMessage(Device device, string? publicBaseUrl)
    {
        var message = $"""
            ⚠️ Unknown network device

            MAC: {device.MacAddress}
            IP: {device.LastIpAddress ?? "Unknown"}
            Hostname: {device.LastHostname ?? "Unknown"}
            First seen: {device.FirstSeenUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}
            """;

        return string.IsNullOrWhiteSpace(publicBaseUrl) ?
            message :
            $"{message}\n\nOpen Lantern to review this device: {publicBaseUrl.TrimEnd('/')}";
    }
}
