namespace LasseVK.JobSystem;

/// <summary>
/// Lifecycle states of an assignment. The set is kept deliberately small: "scheduled",
/// "retrying" and "failed (this attempt)" are derived from the next-run time and attempt
/// count rather than being stored as distinct states.
/// </summary>
public enum AssignmentStatus
{
    /// <summary>Waiting to run; claimable once its next-run time has arrived.</summary>
    Pending,

    /// <summary>Claimed and currently executing (with an active lease).</summary>
    Running,

    /// <summary>Completed successfully (terminal).</summary>
    Succeeded,

    /// <summary>Attempts exhausted or a permanent failure (terminal).</summary>
    DeadLettered
}
