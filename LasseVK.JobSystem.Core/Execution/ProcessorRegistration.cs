namespace LasseVK.JobSystem.Execution;

/// <summary>
/// Describes one registered processor: which job type and processor key it serves, the payload
/// type to deserialize into, and a typed invoker that resolves the processor from a scope and
/// runs it. The invoker is built while the payload type is statically known, so the execution
/// path needs no reflection.
/// </summary>
public sealed class ProcessorRegistration
{
    /// <summary>The job type this processor handles.</summary>
    public required string JobType { get; init; }

    /// <summary>The stable key identifying this processor.</summary>
    public required string ProcessorKey { get; init; }

    /// <summary>The payload type to deserialize the stored payload into before invoking the processor.</summary>
    public required Type PayloadType { get; init; }

    /// <summary>
    /// Resolves the keyed processor from the given scope and runs it for the deserialized payload.
    /// </summary>
    public required Func<IServiceProvider, object, IJobContext, CancellationToken, Task> Invoke { get; init; }
}
