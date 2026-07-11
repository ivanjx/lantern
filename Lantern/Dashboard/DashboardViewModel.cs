using Lantern.Devices;

namespace Lantern.Dashboard;

public sealed record DashboardViewModel(
    IReadOnlyList<Device> Devices,
    int UnknownCount,
    int TrustedCount,
    bool InitialScanCompleted,
    DateTimeOffset? LastSuccessfulPollUtc,
    string? LastPollError,
    DashboardFeedback? Feedback)
{
    public bool IsHealthy => LastSuccessfulPollUtc is not null && LastPollError is null;
}

public sealed record DashboardFeedback(string Message, bool IsError);
