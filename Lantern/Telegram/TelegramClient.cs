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
    private readonly HttpClient httpClient;
    private readonly TelegramOptions options;
    private readonly ILogger<TelegramClient> logger;

    public TelegramClient(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramClient> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.logger = logger;
        this.httpClient.BaseAddress = new Uri("https://api.telegram.org/", UriKind.Absolute);
        this.httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<ServiceResult> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new TelegramSendMessageRequest(options.ChatId, message);
            using var response = await httpClient.PostAsJsonAsync(
                $"bot{options.BotToken}/sendMessage",
                request,
                AppJsonSerializerContext.Default.TelegramSendMessageRequest,
                cancellationToken);
            var apiResponse = await response.Content.ReadFromJsonAsync(
                AppJsonSerializerContext.Default.TelegramApiResponse,
                cancellationToken);

            if (!response.IsSuccessStatusCode || apiResponse is not { Ok: true })
            {
                logger.LogError(
                    "Telegram request failed with HTTP {StatusCode}: {Description}",
                    (int)response.StatusCode,
                    apiResponse?.Description ?? "No response description");
                return new ErrorServiceResult();
            }

            return new SuccessServiceResult();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Telegram request timed out");
            return new ErrorServiceResult();
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Telegram request failed");
            return new ErrorServiceResult();
        }
        catch (System.Text.Json.JsonException exception)
        {
            logger.LogError(exception, "Telegram returned an invalid response");
            return new ErrorServiceResult();
        }
    }
}
