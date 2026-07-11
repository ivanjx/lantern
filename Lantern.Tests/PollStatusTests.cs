using Lantern.Monitoring;

namespace Lantern.Tests;

public sealed class PollStatusTests
{
    [Fact]
    public void RecordSuccess_TracksSuccessfulPoll()
    {
        var status = new PollStatus();
        var completedAtUtc = new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero);

        status.RecordSuccess(completedAtUtc);

        var snapshot = status.GetSnapshot();
        Assert.Equal(completedAtUtc, snapshot.LastSuccessfulPollUtc);
        Assert.True(snapshot.MostRecentPollSucceeded);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public void RecordFailure_PreservesLastSuccessAndMarksLatestPollFailed()
    {
        var status = new PollStatus();
        var completedAtUtc = new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero);
        status.RecordSuccess(completedAtUtc);

        status.RecordFailure("Router unavailable");

        var snapshot = status.GetSnapshot();
        Assert.Equal(completedAtUtc, snapshot.LastSuccessfulPollUtc);
        Assert.False(snapshot.MostRecentPollSucceeded);
        Assert.Equal("Router unavailable", snapshot.LastError);
    }
}
