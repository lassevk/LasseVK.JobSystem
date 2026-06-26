namespace LasseVK.JobSystem;

/// <summary>
/// The entry point for submitting jobs. A submitter only needs this and a configured store and
/// serializer; it does not need any processor registered (processors are resolved later, in the
/// background worker).
/// </summary>
public interface IJobQueue
{
    /// <summary>
    /// Submits a payload as a new job and returns its id. The job is created in a pending state
    /// and is expanded into per-processor assignments later, by the background worker.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="payload">The job payload.</param>
    /// <param name="cancellationToken">Cancels the submit operation.</param>
    /// <returns>The id of the newly created job.</returns>
    Task<Guid> SubmitAsync<T>(T payload, CancellationToken cancellationToken = default) where T : IJob;
}
