namespace LasseVK.JobSystem.Storage;

/// <summary>
/// The persisted form of an assignment: one unit of work bound to a specific processor,
/// produced when a job is expanded. An assignment is self-contained (it carries its own copy
/// of the job type and payload) so the execution path needs no join to run it.
/// </summary>
public sealed record AssignmentRecord
{
    /// <summary>Unique identifier of the assignment.</summary>
    public required Guid Id { get; init; }

    /// <summary>The originating dispatch job.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The job type key, copied from the originating job.</summary>
    public required string JobType { get; init; }

    /// <summary>The processor this assignment is bound to.</summary>
    public required string ProcessorKey { get; init; }

    /// <summary>The serialized payload, copied from the originating job.</summary>
    public required string Payload { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required AssignmentStatus Status { get; init; }

    /// <summary>Number of attempts started so far. Incremented each time the assignment is claimed.</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Earliest time the assignment may be claimed (drives initial delay and retry back-off).</summary>
    public required DateTimeOffset NextRunAt { get; init; }

    /// <summary>The error recorded for the most recent failed attempt, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>The worker currently holding a lease on this row, if any.</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>When the current lease expires, if any.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }
}
