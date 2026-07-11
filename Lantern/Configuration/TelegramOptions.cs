namespace Lantern.Configuration;

internal sealed class TelegramOptions
{
    public const string BotTokenEnvironmentVariable = "TELEGRAM_BOT_TOKEN";
    public const string ChatIdEnvironmentVariable = "TELEGRAM_CHAT_ID";
    public const string PublicBaseUrlEnvironmentVariable = "LANTERN_PUBLIC_BASE_URL";

    public string BotToken { get; set; } = "";

    public long ChatId { get; set; }

    public string? PublicBaseUrl { get; set; }
}
