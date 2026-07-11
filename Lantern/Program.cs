using Lantern.Configuration;
using Lantern.Devices;
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

var app = builder.Build();

await app.Services.GetRequiredService<DeviceRepository>().InitializeAsync();

app.MapGet("/", () => Results.RazorSlice<Home>());

app.Run();
