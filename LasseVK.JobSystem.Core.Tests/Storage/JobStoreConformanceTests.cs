using System.Collections.Concurrent;
using LasseVK.JobSystem.Storage;

namespace LasseVK.JobSystem.Tests.Storage;

/// <summary>
/// Behavioural contract that every <see cref="IJobStore"/> implementation must satisfy.
/// Concrete stores derive from this and provide <see cref="CreateStore"/>; xUnit runs the
/// inherited facts against each implementation.
/// </summary>
public abstract class JobStoreConformanceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    protected abstract IJobStore CreateStore();

    [Fact]
    public async Task Inserted_job_can_be_claimed_then_is_not_claimable_again()
    {
        IJobStore store = CreateStore();
        Guid id = Guid.NewGuid();
        await store.InsertJobAsync(NewJob(id));

        JobRecord? first = await store.ClaimNextJobAsync("w1", T0, T0 + Lease);
        JobRecord? second = await store.ClaimNextJobAsync("w2", T0, T0 + Lease);

        Assert.NotNull(first);
        Assert.Equal(id, first!.Id);
        Assert.Equal("w1", first.LeaseOwner);
        Assert.Equal(JobStatus.Pending, first.Status);
        Assert.Null(second);
    }

    [Fact]
    public async Task Expand_creates_assignments_and_marks_job_assigned()
    {
        IJobStore store = CreateStore();
        Guid jobId = Guid.NewGuid();
        await store.InsertJobAsync(NewJob(jobId));
        await store.ClaimNextJobAsync("w1", T0, T0 + Lease);

        AssignmentRecord a1 = NewAssignment(jobId, "email");
        AssignmentRecord a2 = NewAssignment(jobId, "pushover");
        bool expanded = await store.ExpandJobAsync(jobId, "w1", new[] { a1, a2 });

        JobRecord? job = await store.GetJobAsync(jobId);
        Assert.True(expanded);
        Assert.Equal(JobStatus.Assigned, job!.Status);
        Assert.Null(job.LeaseOwner);
        Assert.NotNull(await store.GetAssignmentAsync(a1.Id));
        Assert.NotNull(await store.GetAssignmentAsync(a2.Id));
    }

    [Fact]
    public async Task Expand_with_no_assignments_marks_job_unhandled()
    {
        IJobStore store = CreateStore();
        Guid jobId = Guid.NewGuid();
        await store.InsertJobAsync(NewJob(jobId));
        await store.ClaimNextJobAsync("w1", T0, T0 + Lease);

        bool expanded = await store.ExpandJobAsync(jobId, "w1", Array.Empty<AssignmentRecord>());

        JobRecord? job = await store.GetJobAsync(jobId);
        Assert.True(expanded);
        Assert.Equal(JobStatus.Unhandled, job!.Status);
    }

    [Fact]
    public async Task Expand_by_a_non_owner_is_rejected()
    {
        IJobStore store = CreateStore();
        Guid jobId = Guid.NewGuid();
        await store.InsertJobAsync(NewJob(jobId));
        await store.ClaimNextJobAsync("w1", T0, T0 + Lease);

        bool expanded = await store.ExpandJobAsync(jobId, "someone-else", new[] { NewAssignment(jobId, "email") });

        Assert.False(expanded);
        Assert.Equal(JobStatus.Pending, (await store.GetJobAsync(jobId))!.Status);
    }

    [Fact]
    public async Task Claimed_assignment_is_running_with_incremented_attempt()
    {
        IJobStore store = CreateStore();
        Guid assignmentId = await SeedSingleAssignment(store, "email", T0);

        AssignmentRecord? claimed = await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease);

        Assert.NotNull(claimed);
        Assert.Equal(assignmentId, claimed!.Id);
        Assert.Equal(AssignmentStatus.Running, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal("w1", claimed.LeaseOwner);
    }

    [Fact]
    public async Task Assignment_is_not_claimable_before_its_next_run_time()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0 + TimeSpan.FromMinutes(5));

        AssignmentRecord? tooEarly = await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease);
        AssignmentRecord? onTime = await store.ClaimNextAssignmentAsync("w1", T0 + TimeSpan.FromMinutes(5), T0 + TimeSpan.FromMinutes(6));

        Assert.Null(tooEarly);
        Assert.NotNull(onTime);
    }

    [Fact]
    public async Task Complete_marks_assignment_succeeded()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        AssignmentRecord claimed = (await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease))!;

        bool completed = await store.CompleteAssignmentAsync(claimed.Id, "w1");

        Assert.True(completed);
        Assert.Equal(AssignmentStatus.Succeeded, (await store.GetAssignmentAsync(claimed.Id))!.Status);
        Assert.Null(await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease));
    }

    [Fact]
    public async Task Reschedule_returns_assignment_to_pending_at_a_later_time()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        AssignmentRecord claimed = (await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease))!;

        DateTimeOffset retryAt = T0 + TimeSpan.FromMinutes(1);
        bool rescheduled = await store.RescheduleAssignmentAsync(claimed.Id, "w1", retryAt, "boom");

        Assert.True(rescheduled);
        AssignmentRecord after = (await store.GetAssignmentAsync(claimed.Id))!;
        Assert.Equal(AssignmentStatus.Pending, after.Status);
        Assert.Equal(retryAt, after.NextRunAt);
        Assert.Equal("boom", after.LastError);
        Assert.Null(await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease));
        Assert.NotNull(await store.ClaimNextAssignmentAsync("w1", retryAt, retryAt + Lease));
    }

    [Fact]
    public async Task DeadLetter_marks_assignment_dead_lettered()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        AssignmentRecord claimed = (await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease))!;

        bool deadLettered = await store.DeadLetterAssignmentAsync(claimed.Id, "w1", "permanent");

        Assert.True(deadLettered);
        AssignmentRecord after = (await store.GetAssignmentAsync(claimed.Id))!;
        Assert.Equal(AssignmentStatus.DeadLettered, after.Status);
        Assert.Equal("permanent", after.LastError);
    }

    [Fact]
    public async Task Mutations_by_a_non_owner_are_rejected()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        AssignmentRecord claimed = (await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease))!;

        Assert.False(await store.CompleteAssignmentAsync(claimed.Id, "intruder"));
        Assert.False(await store.HeartbeatAssignmentAsync(claimed.Id, "intruder", T0 + Lease));
        Assert.Equal(AssignmentStatus.Running, (await store.GetAssignmentAsync(claimed.Id))!.Status);
    }

    [Fact]
    public async Task Heartbeat_extends_the_lease()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        AssignmentRecord claimed = (await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease))!;

        DateTimeOffset extended = T0 + TimeSpan.FromMinutes(2);
        bool ok = await store.HeartbeatAssignmentAsync(claimed.Id, "w1", extended);

        Assert.True(ok);
        Assert.Equal(extended, (await store.GetAssignmentAsync(claimed.Id))!.LeaseExpiresAt);
    }

    [Fact]
    public async Task Expired_running_assignments_are_reclaimed()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        AssignmentRecord claimed = (await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease))!;

        DateTimeOffset afterExpiry = T0 + Lease + TimeSpan.FromSeconds(1);
        int reclaimed = await store.ReclaimExpiredAssignmentsAsync(afterExpiry);

        Assert.Equal(1, reclaimed);
        Assert.Equal(AssignmentStatus.Pending, (await store.GetAssignmentAsync(claimed.Id))!.Status);
        AssignmentRecord? reclaimedClaim = await store.ClaimNextAssignmentAsync("w2", afterExpiry, afterExpiry + Lease);
        Assert.NotNull(reclaimedClaim);
        Assert.Equal(2, reclaimedClaim!.AttemptCount);
    }

    [Fact]
    public async Task A_running_assignment_within_its_lease_is_not_reclaimed()
    {
        IJobStore store = CreateStore();
        await SeedSingleAssignment(store, "email", T0);
        await store.ClaimNextAssignmentAsync("w1", T0, T0 + Lease);

        int reclaimed = await store.ReclaimExpiredAssignmentsAsync(T0 + TimeSpan.FromSeconds(5));

        Assert.Equal(0, reclaimed);
    }

    [Fact]
    public async Task Concurrent_claims_never_return_the_same_assignment()
    {
        IJobStore store = CreateStore();
        Guid jobId = Guid.NewGuid();
        await store.InsertJobAsync(NewJob(jobId));
        await store.ClaimNextJobAsync("setup", T0, T0 + Lease);

        const int count = 200;
        List<AssignmentRecord> assignments = Enumerable.Range(0, count)
            .Select(i => NewAssignment(jobId, $"p{i}", T0))
            .ToList();
        await store.ExpandJobAsync(jobId, "setup", assignments);

        ConcurrentBag<Guid> claimed = new();

        async Task Worker(string name)
        {
            while (true)
            {
                AssignmentRecord? a = await store.ClaimNextAssignmentAsync(name, T0, T0 + Lease);
                if (a is null)
                {
                    break;
                }

                claimed.Add(a.Id);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Worker($"w{i}")));

        Assert.Equal(count, claimed.Count);
        Assert.Equal(count, claimed.Distinct().Count());
    }

    private async Task<Guid> SeedSingleAssignment(IJobStore store, string processorKey, DateTimeOffset nextRunAt)
    {
        Guid jobId = Guid.NewGuid();
        await store.InsertJobAsync(NewJob(jobId));
        await store.ClaimNextJobAsync("setup", T0, T0 + Lease);
        AssignmentRecord assignment = NewAssignment(jobId, processorKey, nextRunAt);
        await store.ExpandJobAsync(jobId, "setup", new[] { assignment });
        return assignment.Id;
    }

    private static JobRecord NewJob(Guid id, string jobType = "job", string payload = "{}")
    {
        return new JobRecord
        {
            Id = id,
            JobType = jobType,
            Payload = payload,
            Status = JobStatus.Pending
        };
    }

    private static AssignmentRecord NewAssignment(Guid jobId, string processorKey, DateTimeOffset? nextRunAt = null, string jobType = "job", string payload = "{}")
    {
        return new AssignmentRecord
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            JobType = jobType,
            ProcessorKey = processorKey,
            Payload = payload,
            Status = AssignmentStatus.Pending,
            AttemptCount = 0,
            NextRunAt = nextRunAt ?? T0
        };
    }
}
