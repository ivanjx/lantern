using Lantern.Configuration;
using Lantern.Devices;
using Lantern.MikroTik;
using Lantern.Monitoring;
using Lantern.Slices;
using Lantern.Telegram;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration.AddEnvironmentVariables();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

builder.Services
    .AddOptions<DatabaseOptions>()
    .Configure(options =>
        options.Path = builder.Configuration[DatabaseOptions.PathEnvironmentVariable] ?? options.Path)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Path), "DATABASE_PATH is required.")
    .ValidateOnStart();
builder.Services.AddSingleton<DeviceRepository>();
builder.Services.AddSingleton<DeviceDetectionService>();
builder.Services.AddSingleton<DeviceMonitoringService>();
builder.Services
    .AddOptions<TelegramOptions>()
    .Configure(options =>
    {
        options.BotToken = builder.Configuration[TelegramOptions.BotTokenEnvironmentVariable] ?? "";
        options.ChatId = long.TryParse(builder.Configuration[TelegramOptions.ChatIdEnvironmentVariable], out var chatId) ?
            chatId :
            0;
        options.PublicBaseUrl = builder.Configuration[TelegramOptions.PublicBaseUrlEnvironmentVariable];
    })
    .Validate(options => !string.IsNullOrWhiteSpace(options.BotToken), "TELEGRAM_BOT_TOKEN is required.")
    .Validate(options => options.ChatId != 0, "TELEGRAM_CHAT_ID must be a non-zero integer.")
    .Validate(options => string.IsNullOrWhiteSpace(options.PublicBaseUrl) ||
        Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out _),
        "LANTERN_PUBLIC_BASE_URL must be an absolute URL when set.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ITelegramClient, TelegramClient>();
builder.Services.AddSingleton<TelegramNotificationService>();
builder.Logging.AddFilter("System.Net.Http.HttpClient.ITelegramClient", LogLevel.None);
builder.Services
    .AddOptions<LanternOptions>()
    .Configure(options =>
    {
        var configuredValue = builder.Configuration[LanternOptions.PollIntervalSecondsEnvironmentVariable];

        if (int.TryParse(configuredValue, out var pollIntervalSeconds))
        {
            options.PollIntervalSeconds = pollIntervalSeconds;
        }
    })
    .Validate(options => options.PollIntervalSeconds >= LanternOptions.MinimumPollIntervalSeconds,
        "LANTERN_POLL_INTERVAL_SECONDS must be at least 5.")
    .ValidateOnStart();
builder.Services
    .AddOptions<MikroTikOptions>()
    .Configure(options =>
    {
        options.BaseUrl = builder.Configuration[MikroTikOptions.BaseUrlEnvironmentVariable] ?? "";
        options.Username = builder.Configuration[MikroTikOptions.UsernameEnvironmentVariable] ?? "";
        options.Password = builder.Configuration[MikroTikOptions.PasswordEnvironmentVariable] ?? "";
        options.AllowInvalidCertificate = bool.TryParse(
            builder.Configuration[MikroTikOptions.AllowInvalidCertificateEnvironmentVariable],
            out var allowInvalidCertificate) && allowInvalidCertificate;
    })
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
        "MIKROTIK_BASE_URL must be an absolute HTTPS URL.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "MIKROTIK_USERNAME is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "MIKROTIK_PASSWORD is required.")
    .ValidateOnStart();
builder.Services
    .AddHttpClient<IMikroTikClient, MikroTikClient>()
    .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MikroTikOptions>>().Value;
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = options.AllowInvalidCertificate ?
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator :
                null
        };
    });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PollStatus>();
builder.Services.AddHostedService<DeviceMonitorJob>();

var app = builder.Build();

await app.Services.GetRequiredService<DeviceRepository>().InitializeAsync();

app.MapGet("/", () => Results.RazorSlice<Home>());
app.MapGet("/health", async (
    DeviceRepository repository,
    PollStatus pollStatus,
    Microsoft.Extensions.Options.IOptions<LanternOptions> options,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var databaseAccessible = await repository.IsAccessibleAsync(cancellationToken);
    var snapshot = pollStatus.GetSnapshot();
    var maximumAge = TimeSpan.FromSeconds(Math.Max(options.Value.PollIntervalSeconds * 3, 60));
    var pollIsRecent = snapshot.LastSuccessfulPollUtc is { } lastSuccessfulPollUtc &&
        timeProvider.GetUtcNow() - lastSuccessfulPollUtc <= maximumAge;
    var healthy = databaseAccessible && pollIsRecent;
    var content = $"""
        status={((healthy ? "healthy" : "unhealthy"))}
        database_accessible={databaseAccessible.ToString().ToLowerInvariant()}
        last_successful_poll_utc={snapshot.LastSuccessfulPollUtc?.ToString("O") ?? "never"}
        most_recent_poll_succeeded={snapshot.MostRecentPollSucceeded.ToString().ToLowerInvariant()}
        """;

    return Results.Text(content, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();
