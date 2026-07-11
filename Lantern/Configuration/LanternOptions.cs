namespace Lantern.Configuration;

internal sealed class LanternOptions
{
    public const string PollIntervalSecondsEnvironmentVariable = "LANTERN_POLL_INTERVAL_SECONDS";
    public const int DefaultPollIntervalSeconds = 15;
    public const int MinimumPollIntervalSeconds = 5;

    public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;
}
