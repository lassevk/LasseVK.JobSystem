namespace LasseVK.JobSystem.Tests.Execution;

/// <summary>A shared, thread-safe sink the test processors write to so tests can observe execution.</summary>
public sealed class ExecutionLog
{
    private readonly List<string> _messages = new();
    private readonly Lock _gate = new();

    public void Record(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }
}

/// <summary>A controllable clock for deterministic dispatch, retry, and back-off tests.</summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
    }
}

/// <summary>Succeeds, recording the processor key, payload, and attempt number.</summary>
public sealed class RecordingProcessor : IJobProcessor<SampleJob>
{
    private readonly ExecutionLog _log;

    public RecordingProcessor(ExecutionLog log)
    {
        _log = log;
    }

    public static string ProcessorKey => "recording";

    public Task ProcessAsync(SampleJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        _log.Record($"{context.ProcessorKey}:{payload.Message}:attempt{context.AttemptNumber}");
        return Task.CompletedTask;
    }
}

/// <summary>A second processor for the same job type, to exercise fan-out.</summary>
public sealed class SecondRecordingProcessor : IJobProcessor<SampleJob>
{
    private readonly ExecutionLog _log;

    public SecondRecordingProcessor(ExecutionLog log)
    {
        _log = log;
    }

    public static string ProcessorKey => "recording-2";

    public Task ProcessAsync(SampleJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        _log.Record($"{context.ProcessorKey}:{payload.Message}:attempt{context.AttemptNumber}");
        return Task.CompletedTask;
    }
}

/// <summary>Always throws, to exercise retry and dead-lettering.</summary>
public sealed class AlwaysFailingProcessor : IJobProcessor<SampleJob>
{
    public static string ProcessorKey => "always-failing";

    public Task ProcessAsync(SampleJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("boom");
    }
}

/// <summary>Shares <see cref="RecordingProcessor"/>'s key and job type, to exercise duplicate detection.</summary>
public sealed class DuplicateKeyProcessor : IJobProcessor<SampleJob>
{
    public static string ProcessorKey => "recording";

    public Task ProcessAsync(SampleJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>A second job type, used to build a processor that handles more than one payload type.</summary>
public sealed class OtherJob : IJob
{
    public static string JobType => "other-job";

    public required string Note { get; init; }
}

/// <summary>Implements <see cref="IJobProcessor{T}"/> for two payload types, which is not allowed.</summary>
public sealed class MultiPayloadProcessor : IJobProcessor<SampleJob>, IJobProcessor<OtherJob>
{
    public static string ProcessorKey => "multi-payload";

    public Task ProcessAsync(SampleJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ProcessAsync(OtherJob payload, IJobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
