namespace LasseVK.JobSystem.Execution;

/// <summary>
/// Tuning knobs for the background worker. All values have sensible defaults, so a caller can
/// register the job system without configuring anything.
/// </summary>
public sealed class JobSystemOptions
{
    /// <summary>How long to wait before polling again when there is no work to do.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a claimed job or assignment stays leased to this worker. The lease is renewed
    /// (heartbeated) while an assignment runs; if the worker crashes, another worker may reclaim
    /// the work once the lease expires.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// On shutdown, how long an in-flight assignment is allowed to keep running before its
    /// cancellation token is signalled.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many attempts an assignment gets before it is dead-lettered. An attempt is counted when
    /// the assignment is claimed for execution, so this is the total number of executions tried.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The delay before the first retry. Later retries grow exponentially up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The cap on the exponential retry back-off.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(10);
}
