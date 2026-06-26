namespace LasseVK.JobSystem;

/// <summary>
/// Lifecycle states of a dispatch job. A job only ever writes to the store, so it has no
/// failure-handling metadata; "currently being expanded" is represented as <see cref="Pending"/>
/// plus an active lease rather than a separate state.
/// </summary>
public enum JobStatus
{
    /// <summary>Submitted, not yet expanded into assignments.</summary>
    Pending,

    /// <summary>Expanded into one assignment per registered processor (terminal).</summary>
    Assigned,

    /// <summary>No processor was registered for the job type (terminal).</summary>
    Unhandled
}
