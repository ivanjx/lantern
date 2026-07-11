using Lantern.Configuration;
using Lantern.Devices;
using Lantern.MikroTik;
using Lantern.Slices;

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
    .AddHttpClient<MikroTikClient>()
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

var app = builder.Build();

await app.Services.GetRequiredService<DeviceRepository>().InitializeAsync();

app.MapGet("/", () => Results.RazorSlice<Home>());

app.Run();
