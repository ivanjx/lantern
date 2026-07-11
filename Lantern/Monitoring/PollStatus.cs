namespace Lantern.Monitoring;

internal sealed class PollStatus
{
    private readonly Lock syncRoot = new();
    private DateTimeOffset? lastSuccessfulPollUtc;
    private string? lastError;

    public PollStatusSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new PollStatusSnapshot(lastSuccessfulPollUtc, lastError);
        }
    }

    public void RecordSuccess(DateTimeOffset completedAtUtc)
    {
        lock (syncRoot)
        {
            lastSuccessfulPollUtc = completedAtUtc;
            lastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (syncRoot)
        {
            lastError = error;
        }
    }
}

internal sealed record PollStatusSnapshot(
    DateTimeOffset? LastSuccessfulPollUtc,
    string? LastError)
{
    public bool MostRecentPollSucceeded => LastSuccessfulPollUtc is not null && LastError is null;
}
