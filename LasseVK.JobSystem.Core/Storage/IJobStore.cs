namespace LasseVK.JobSystem.Storage;

/// <summary>
/// Persistence contract for jobs and assignments. Every operation is a single atomic call:
/// no lock is held across calls and no multi-call read-modify-write is required, so each
/// operation maps to one SQL statement or one transaction. All times are supplied by the
/// caller (UTC) to keep implementations free of any clock or policy.
/// </summary>
public interface IJobStore
{
    // --- Submission ---

    /// <summary>Inserts a new job. Throws if a job with the same id already exists.</summary>
    Task InsertJobAsync(JobRecord job, CancellationToken cancellationToken = default);

    /// <summary>Reads a job by id, or <c>null</c> if it does not exist.</summary>
    Task<JobRecord?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    // --- Dispatch ---

    /// <summary>
    /// Atomically claims the next <see cref="JobStatus.Pending"/> job that is not currently
    /// leased, stamping the given lease, and returns it (still <see cref="JobStatus.Pending"/>).
    /// Returns <c>null</c> when there is nothing to claim.
    /// </summary>
    Task<JobRecord?> ClaimNextJobAsync(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts the assignments and sets the job to <see cref="JobStatus.Assigned"/>
    /// (or <see cref="JobStatus.Unhandled"/> when the collection is empty), clearing the lease.
    /// Returns <c>false</c> if the job is no longer <see cref="JobStatus.Pending"/> or is no
    /// longer owned by <paramref name="leaseOwner"/>. Inserting assignment ids that already
    /// exist is treated as an idempotent upsert.
    /// </summary>
    Task<bool> ExpandJobAsync(Guid jobId, string leaseOwner, IReadOnlyCollection<AssignmentRecord> assignments, CancellationToken cancellationToken = default);

    // --- Execution ---

    /// <summary>Reads an assignment by id, or <c>null</c> if it does not exist.</summary>
    Task<AssignmentRecord?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the next due (<c>NextRunAt &lt;= now</c>) <see cref="AssignmentStatus.Pending"/>
    /// assignment that is not currently leased, setting it to <see cref="AssignmentStatus.Running"/>,
    /// incrementing its attempt count, and stamping the lease. Returns <c>null</c> when there is
    /// nothing due to claim.
    /// </summary>
    Task<AssignmentRecord?> ClaimNextAssignmentAsync(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default);

    /// <summary>Renews the lease on a running assignment. Returns <c>false</c> if not owned by the caller.</summary>
    Task<bool> HeartbeatAssignmentAsync(Guid assignmentId, string leaseOwner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default);

    /// <summary>Marks a running assignment <see cref="AssignmentStatus.Succeeded"/>. Returns <c>false</c> if not owned by the caller.</summary>
    Task<bool> CompleteAssignmentAsync(Guid assignmentId, string leaseOwner, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a running assignment to <see cref="AssignmentStatus.Pending"/> with a new
    /// <paramref name="nextRunAt"/> (a retry), recording the error and clearing the lease.
    /// Returns <c>false</c> if not owned by the caller.
    /// </summary>
    Task<bool> RescheduleAssignmentAsync(Guid assignmentId, string leaseOwner, DateTimeOffset nextRunAt, string? lastError, CancellationToken cancellationToken = default);

    /// <summary>Marks a running assignment <see cref="AssignmentStatus.DeadLettered"/>. Returns <c>false</c> if not owned by the caller.</summary>
    Task<bool> DeadLetterAssignmentAsync(Guid assignmentId, string leaseOwner, string? lastError, CancellationToken cancellationToken = default);

    // --- Recovery ---

    /// <summary>
    /// Resets assignments that are <see cref="AssignmentStatus.Running"/> with an expired lease
    /// back to <see cref="AssignmentStatus.Pending"/> (crashed-worker recovery), and returns how
    /// many were reclaimed.
    /// </summary>
    Task<int> ReclaimExpiredAssignmentsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
