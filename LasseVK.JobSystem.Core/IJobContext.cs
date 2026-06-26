namespace LasseVK.JobSystem;

/// <summary>
/// Per-execution context passed to a processor. Exposes the identity of the assignment being
/// processed and how many attempts have been made. The job instance identity lives here rather
/// than on the payload, keeping payloads pure domain data.
/// </summary>
public interface IJobContext
{
    /// <summary>Identifier of the originating dispatch job.</summary>
    Guid JobId { get; }

    /// <summary>Identifier of this specific assignment (one assignment per processor).</summary>
    Guid AssignmentId { get; }

    /// <summary>The key of the processor handling this assignment.</summary>
    string ProcessorKey { get; }

    /// <summary>The current attempt number, starting at 1 for the first attempt.</summary>
    int AttemptNumber { get; }
}
