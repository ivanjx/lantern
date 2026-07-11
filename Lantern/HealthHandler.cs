using Lantern.Configuration;
using Lantern.Devices;
using Lantern.Monitoring;
using Microsoft.Extensions.Options;

namespace Lantern;

internal sealed class HealthHandler(
    DeviceRepository _repository,
    PollStatus _pollStatus,
    IOptions<LanternOptions> _options,
    TimeProvider _timeProvider)
{
    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var databaseAccessible = await _repository.IsAccessibleAsync(cancellationToken);
        var snapshot = _pollStatus.GetSnapshot();
        var maximumAge = TimeSpan.FromSeconds(Math.Max(_options.Value.PollIntervalSeconds * 3, 60));
        var pollIsRecent = snapshot.LastSuccessfulPollUtc is { } lastSuccessfulPollUtc &&
            _timeProvider.GetUtcNow() - lastSuccessfulPollUtc <= maximumAge;
        var healthy = databaseAccessible && pollIsRecent;
        var content = $"""
            status={((healthy ? "healthy" : "unhealthy"))}
            database_accessible={databaseAccessible.ToString().ToLowerInvariant()}
            last_successful_poll_utc={snapshot.LastSuccessfulPollUtc?.ToString("O") ?? "never"}
            most_recent_poll_succeeded={snapshot.MostRecentPollSucceeded.ToString().ToLowerInvariant()}
            """;

        return Results.Text(content, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
}
