namespace LasseVK.JobSystem.Storage;

/// <summary>
/// The persisted form of a dispatch job. The payload is stored in its already-serialized
/// form; serialization is the concern of a layer above the store.
/// </summary>
public sealed record JobRecord
{
    /// <summary>Unique identifier of the job (the id returned from submit).</summary>
    public required Guid Id { get; init; }

    /// <summary>The job type key, taken from the payload's <c>IJob.JobType</c>.</summary>
    public required string JobType { get; init; }

    /// <summary>The serialized payload.</summary>
    public required string Payload { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>The worker currently holding a lease on this row, if any.</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>When the current lease expires, if any.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }
}
