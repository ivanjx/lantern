using System.Text.Json.Serialization;

namespace Lantern.Telegram;

internal sealed record TelegramSendMessageRequest(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text);

internal sealed class TelegramApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
