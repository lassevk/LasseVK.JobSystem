using LasseVK.JobSystem.DependencyInjection;
using LasseVK.JobSystem.Execution;
using LasseVK.JobSystem.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LasseVK.JobSystem.Tests.Execution;

public class JobRunnerTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ServiceProvider Build(
        TimeProvider timeProvider,
        Action<JobSystemBuilder> configure,
        Action<JobSystemOptions>? configureOptions = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<ExecutionLog>();
        services.AddSingleton(timeProvider);

        JobSystemBuilder builder = services.AddJobSystem(configureOptions ?? (_ => { }));
        builder.UseInMemoryStore();
        configure(builder);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Dispatch_expands_a_pending_job_into_one_pending_assignment_per_processor()
    {
        TestTimeProvider time = new(Origin);
        ServiceProvider provider = Build(time, builder => builder.AddProcessor<RecordingProcessor>());
        IJobStore store = provider.GetRequiredService<IJobStore>();
        JobRunner runner = provider.GetRequiredService<JobRunner>();

        Guid jobId = await provider.GetRequiredService<IJobQueue>().SubmitAsync(new SampleJob { Message = "hi" });

        Assert.True(await runner.TryDispatchAsync());

        JobRecord? job = await store.GetJobAsync(jobId);
        Assert.Equal(JobStatus.Assigned, job!.Status);

        Guid assignmentId = DeterministicGuid.Create(jobId, RecordingProcessor.ProcessorKey);
        AssignmentRecord? assignment = await store.GetAssignmentAsync(assignmentId);
        Assert.NotNull(assignment);
        Assert.Equal(AssignmentStatus.Pending, assignment!.Status);
        Assert.Equal(0, assignment.AttemptCount);
        Assert.Equal(RecordingProcessor.ProcessorKey, assignment.ProcessorKey);
    }

    [Fact]
    public async Task Dispatch_marks_a_job_unhandled_when_no_processor_is_registered()
    {
        TestTimeProvider time = new(Origin);
        ServiceProvider provider = Build(time, _ => { });
        IJobStore store = provider.GetRequiredService<IJobStore>();
        JobRunner runner = provider.GetRequiredService<JobRunner>();

        Guid jobId = await provider.GetRequiredService<IJobQueue>().SubmitAsync(new SampleJob { Message = "orphan" });

        Assert.True(await runner.TryDispatchAsync());

        JobRecord? job = await store.GetJobAsync(jobId);
        Assert.Equal(JobStatus.Unhandled, job!.Status);
    }

    [Fact]
    public async Task Execute_runs_the_processor_and_marks_the_assignment_succeeded()
    {
        TestTimeProvider time = new(Origin);
        ServiceProvider provider = Build(time, builder => builder.AddProcessor<RecordingProcessor>());
        IJobStore store = provider.GetRequiredService<IJobStore>();
        JobRunner runner = provider.GetRequiredService<JobRunner>();
        ExecutionLog log = provider.GetRequiredService<ExecutionLog>();

        Guid jobId = await provider.GetRequiredService<IJobQueue>().SubmitAsync(new SampleJob { Message = "hi" });
        await runner.TryDispatchAsync();

        Assert.True(await runner.TryExecuteAsync());
        Assert.False(await runner.TryExecuteAsync());

        Assert.Contains("recording:hi:attempt1", log.Messages);

        Guid assignmentId = DeterministicGuid.Create(jobId, RecordingProcessor.ProcessorKey);
        AssignmentRecord? assignment = await store.GetAssignmentAsync(assignmentId);
        Assert.Equal(AssignmentStatus.Succeeded, assignment!.Status);
        Assert.Equal(1, assignment.AttemptCount);
    }

    [Fact]
    public async Task Fan_out_runs_every_registered_processor_for_the_job_type()
    {
        TestTimeProvider time = new(Origin);
        ServiceProvider provider = Build(time, builder =>
        {
            builder.AddProcessor<RecordingProcessor>();
            builder.AddProcessor<SecondRecordingProcessor>();
        });
        JobRunner runner = provider.GetRequiredService<JobRunner>();
        ExecutionLog log = provider.GetRequiredService<ExecutionLog>();

        await provider.GetRequiredService<IJobQueue>().SubmitAsync(new SampleJob { Message = "hi" });
        await runner.TryDispatchAsync();

        while (await runner.TryExecuteAsync())
        {
        }

        Assert.Contains("recording:hi:attempt1", log.Messages);
        Assert.Contains("recording-2:hi:attempt1", log.Messages);
    }

    [Fact]
    public async Task A_failing_processor_is_retried_with_back_off_and_then_dead_lettered()
    {
        TestTimeProvider time = new(Origin);
        ServiceProvider provider = Build(
            time,
            builder => builder.AddProcessor<AlwaysFailingProcessor>(),
            options =>
            {
                options.MaxAttempts = 2;
                options.BaseRetryDelay = TimeSpan.FromSeconds(1);
            });
        IJobStore store = provider.GetRequiredService<IJobStore>();
        JobRunner runner = provider.GetRequiredService<JobRunner>();

        Guid jobId = await provider.GetRequiredService<IJobQueue>().SubmitAsync(new SampleJob { Message = "fail" });
        await runner.TryDispatchAsync();
        Guid assignmentId = DeterministicGuid.Create(jobId, AlwaysFailingProcessor.ProcessorKey);

        // First attempt fails and is rescheduled one second out.
        Assert.True(await runner.TryExecuteAsync());
        AssignmentRecord? afterFirst = await store.GetAssignmentAsync(assignmentId);
        Assert.Equal(AssignmentStatus.Pending, afterFirst!.Status);
        Assert.Equal(1, afterFirst.AttemptCount);
        Assert.Equal(Origin + TimeSpan.FromSeconds(1), afterFirst.NextRunAt);

        // Not yet due, so nothing is claimed.
        Assert.False(await runner.TryExecuteAsync());

        // After the back-off the second (final) attempt fails and dead-letters.
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await runner.TryExecuteAsync());
        AssignmentRecord? afterSecond = await store.GetAssignmentAsync(assignmentId);
        Assert.Equal(AssignmentStatus.DeadLettered, afterSecond!.Status);
        Assert.Equal(2, afterSecond.AttemptCount);
        Assert.Contains("boom", afterSecond.LastError);
    }

    [Fact]
    public void Registering_two_processors_for_the_same_job_type_and_key_throws()
    {
        ServiceCollection services = new();
        services.AddSingleton<ExecutionLog>();
        JobSystemBuilder builder = services.AddJobSystem();

        builder.AddProcessor<RecordingProcessor>();

        Assert.Throws<InvalidOperationException>(() => builder.AddProcessor<DuplicateKeyProcessor>());
    }

    [Fact]
    public void Registering_a_processor_that_handles_more_than_one_payload_type_throws()
    {
        ServiceCollection services = new();
        JobSystemBuilder builder = services.AddJobSystem();

        Assert.Throws<InvalidOperationException>(() => builder.AddProcessor<MultiPayloadProcessor>());
    }
}
