namespace LasseVK.JobSystem.Execution;

/// <summary>The default <see cref="IJobContext"/>, built per assignment execution.</summary>
internal sealed class JobContext : IJobContext
{
    public JobContext(Guid jobId, Guid assignmentId, string processorKey, int attemptNumber)
    {
        JobId = jobId;
        AssignmentId = assignmentId;
        ProcessorKey = processorKey;
        AttemptNumber = attemptNumber;
    }

    public Guid JobId { get; }

    public Guid AssignmentId { get; }

    public string ProcessorKey { get; }

    public int AttemptNumber { get; }
}
