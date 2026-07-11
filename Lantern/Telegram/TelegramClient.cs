using System.Net.Http.Json;
using Lantern.Configuration;
using Microsoft.Extensions.Options;

namespace Lantern.Telegram;

internal interface ITelegramClient
{
    Task<ServiceResult> SendMessageAsync(string message, CancellationToken cancellationToken = default);
}

internal sealed class TelegramClient : ITelegramClient
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramClient> _logger;

    public TelegramClient(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://api.telegram.org/", UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<ServiceResult> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new TelegramSendMessageRequest(_options.ChatId, message);
            using var response = await _httpClient.PostAsJsonAsync(
                $"bot{_options.BotToken}/sendMessage",
                request,
                AppJsonSerializerContext.Default.TelegramSendMessageRequest,
                cancellationToken);
            var apiResponse = await response.Content.ReadFromJsonAsync(
                AppJsonSerializerContext.Default.TelegramApiResponse,
                cancellationToken);

            if (!response.IsSuccessStatusCode || apiResponse is not { Ok: true })
            {
                _logger.LogError(
                    "Telegram request failed with HTTP {StatusCode}: {Description}",
                    (int)response.StatusCode,
                    apiResponse?.Description ?? "No response description");
                return new ErrorServiceResult();
            }

            return new SuccessServiceResult();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Telegram request timed out");
            return new ErrorServiceResult();
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Telegram request failed");
            return new ErrorServiceResult();
        }
        catch (System.Text.Json.JsonException exception)
        {
            _logger.LogError(exception, "Telegram returned an invalid response");
            return new ErrorServiceResult();
        }
    }
}
