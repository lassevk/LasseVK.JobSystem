using LasseVK.JobSystem.Serialization;
using LasseVK.JobSystem.Storage;

namespace LasseVK.JobSystem;

/// <summary>
/// Default <see cref="IJobQueue"/>: serializes the payload and inserts a pending job into the store.
/// </summary>
public sealed class JobQueue : IJobQueue
{
    private readonly IJobStore _store;
    private readonly IJobSerializer _serializer;

    public JobQueue(IJobStore store, IJobSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(serializer);

        _store = store;
        _serializer = serializer;
    }

    public async Task<Guid> SubmitAsync<T>(T payload, CancellationToken cancellationToken = default) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(payload);

        Guid id = Guid.NewGuid();
        JobRecord job = new()
        {
            Id = id,
            JobType = T.JobType,
            Payload = _serializer.Serialize(payload, typeof(T)),
            Status = JobStatus.Pending
        };

        await _store.InsertJobAsync(job, cancellationToken);
        return id;
    }
}
