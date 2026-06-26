using LasseVK.JobSystem.Serialization;
using LasseVK.JobSystem.Storage;

namespace LasseVK.JobSystem.Tests.Submission;

public class SubmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submit_inserts_a_pending_job_with_the_job_type_and_serialized_payload()
    {
        InMemoryJobStore store = new();
        IJobSerializer serializer = new JsonJobSerializer();
        IJobQueue queue = new JobQueue(store, serializer);

        Guid id = await queue.SubmitAsync(new SampleJob { Message = "hello" });

        JobRecord? job = await store.GetJobAsync(id);
        Assert.NotNull(job);
        Assert.Equal(id, job!.Id);
        Assert.Equal("sample-job", job.JobType);
        Assert.Equal(JobStatus.Pending, job.Status);

        SampleJob roundTrip = (SampleJob)serializer.Deserialize(job.Payload, typeof(SampleJob));
        Assert.Equal("hello", roundTrip.Message);
    }

    [Fact]
    public async Task Submitted_job_is_immediately_claimable()
    {
        InMemoryJobStore store = new();
        IJobQueue queue = new JobQueue(store, new JsonJobSerializer());

        Guid id = await queue.SubmitAsync(new SampleJob { Message = "x" });

        JobRecord? claimed = await store.ClaimNextJobAsync("w1", Now, Now + TimeSpan.FromMinutes(1));
        Assert.NotNull(claimed);
        Assert.Equal(id, claimed!.Id);
    }
}
