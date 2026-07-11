using System.Net;
using Lantern.Configuration;
using Lantern.Telegram;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lantern.Tests;

public sealed class TelegramClientTests
{
    [Fact]
    public async Task SendMessageAsync_ResolvesBotTokenAsTelegramApiPath()
    {
        var handler = new RecordingHandler();
        var client = new TelegramClient(
            new HttpClient(handler),
            Options.Create(new TelegramOptions
            {
                BotToken = "8757487465:secret",
                ChatId = 123
            }),
            NullLogger<TelegramClient>.Instance);

        var result = await client.SendMessageAsync("Hello");

        Assert.IsType<SuccessServiceResult>(result);
        Assert.Equal(
            "https://api.telegram.org/bot8757487465:secret/sendMessage",
            handler.RequestUri?.AbsoluteUri);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
