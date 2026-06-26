namespace LasseVK.JobSystem;

/// <summary>
/// Marker interface for a job payload. An implementation carries the data describing a
/// unit of work and declares a stable <see cref="JobType"/> used to persist and route it.
/// </summary>
public interface IJob
{
    /// <summary>
    /// A stable identifier for this kind of job, for example "send-email". It is persisted
    /// with the job and used at execution time to resolve the payload type, so it must not
    /// change once jobs of this type exist in storage.
    /// </summary>
    static abstract string JobType { get; }
}
