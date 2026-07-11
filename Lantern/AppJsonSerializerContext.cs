using System.Text.Json.Serialization;
using Lantern.MikroTik;
using Lantern.Telegram;

namespace Lantern;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MikroTikLeaseResponse[]))]
[JsonSerializable(typeof(TelegramSendMessageRequest))]
[JsonSerializable(typeof(TelegramApiResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
