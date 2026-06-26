namespace LasseVK.JobSystem.Tests;

/// <summary>A sample payload used to verify the foundational contracts compile and behave.</summary>
public sealed class SampleJob : IJob
{
    public static string JobType => "sample-job";

    public required string Message { get; init; }
}

/// <summary>A sample processor for <see cref="SampleJob"/>.</summary>
public sealed class SampleProcessor : IJobProcessor<SampleJob>
{
    public static string ProcessorKey => "sample-processor";

    public Task ProcessAsync(SampleJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class FoundationTests
{
    [Fact]
    public void Job_exposes_its_static_job_type()
    {
        Assert.Equal("sample-job", SampleJob.JobType);
    }

    [Fact]
    public void Processor_exposes_its_static_processor_key()
    {
        Assert.Equal("sample-processor", SampleProcessor.ProcessorKey);
    }

    [Fact]
    public void Job_type_is_readable_through_a_generic_constraint()
    {
        // This mirrors how startup discovery reads the key from the type without an instance.
        Assert.Equal("sample-job", GetJobType<SampleJob>());
    }

    private static string GetJobType<T>() where T : IJob
    {
        return T.JobType;
    }
}
