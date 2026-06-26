namespace LasseVK.JobSystem;

/// <summary>
/// Non-generic base for job processors. Carries the stable <see cref="ProcessorKey"/> that
/// identifies this processor independently of the payload type(s) it handles.
/// </summary>
public interface IJobProcessor
{
    /// <summary>
    /// A stable identifier for this processor, for example "email-notifier". It is persisted
    /// on each assignment and used to route the assignment back to this processor, so it must
    /// not change once assignments reference it.
    /// </summary>
    static abstract string ProcessorKey { get; }
}

/// <summary>
/// Processes job payloads of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The payload type handled by this processor.</typeparam>
public interface IJobProcessor<in T> : IJobProcessor where T : IJob
{
    /// <summary>
    /// Executes the work for a single assignment. Returning normally marks the assignment as
    /// succeeded; throwing marks the attempt as failed. Because the system guarantees
    /// at-least-once execution, implementations must be idempotent.
    /// </summary>
    /// <param name="payload">The deserialized job payload.</param>
    /// <param name="context">Context describing the assignment being processed.</param>
    /// <param name="cancellationToken">Signalled when the worker is shutting down.</param>
    Task ProcessAsync(T payload, IJobContext context, CancellationToken cancellationToken);
}
