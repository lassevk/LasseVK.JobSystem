using LasseVK.JobSystem.Serialization;
using LasseVK.JobSystem.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LasseVK.JobSystem.Execution;

/// <summary>
/// The unit of work behind the background worker. Each call performs at most one store-driven
/// step (reclaim, dispatch, or execute), so the steps are individually testable and the hosted
/// service is just a loop around them. A single instance carries one lease owner identity.
/// </summary>
public sealed class JobRunner
{
    private readonly IJobStore _store;
    private readonly ProcessorRegistry _registry;
    private readonly IJobSerializer _serializer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobSystemOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _owner;

    public JobRunner(
        IJobStore store,
        ProcessorRegistry registry,
        IJobSerializer serializer,
        IServiceScopeFactory scopeFactory,
        JobSystemOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _registry = registry;
        _serializer = serializer;
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    /// <summary>The lease owner identity used by this runner for all claims.</summary>
    public string Owner => _owner;

    /// <summary>Resets assignments left running by a crashed worker. Returns how many were reclaimed.</summary>
    public Task<int> ReclaimExpiredAsync(CancellationToken cancellationToken = default)
    {
        return _store.ReclaimExpiredAssignmentsAsync(_timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// Claims one pending job and expands it into per-processor assignments (or marks it unhandled
    /// when no processor is registered for its type). Returns <c>true</c> if a job was dispatched.
    /// </summary>
    public async Task<bool> TryDispatchAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        JobRecord? job = await _store.ClaimNextJobAsync(_owner, now, now + _options.LeaseDuration, cancellationToken);
        if (job is null)
        {
            return false;
        }

        IReadOnlyList<ProcessorRegistration> registrations = _registry.ForJobType(job.JobType);
        List<AssignmentRecord> assignments = new(registrations.Count);
        foreach (ProcessorRegistration registration in registrations)
        {
            assignments.Add(new AssignmentRecord
            {
                Id = DeterministicGuid.Create(job.Id, registration.ProcessorKey),
                JobId = job.Id,
                JobType = job.JobType,
                ProcessorKey = registration.ProcessorKey,
                Payload = job.Payload,
                Status = AssignmentStatus.Pending,
                AttemptCount = 0,
                NextRunAt = now
            });
        }

        await _store.ExpandJobAsync(job.Id, _owner, assignments, cancellationToken);
        return true;
    }

    /// <summary>
    /// Claims one due assignment, runs its processor, and records the outcome (succeeded, retried,
    /// or dead-lettered). Returns <c>true</c> if an assignment was claimed.
    /// </summary>
    public async Task<bool> TryExecuteAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        AssignmentRecord? assignment = await _store.ClaimNextAssignmentAsync(_owner, now, now + _options.LeaseDuration, cancellationToken);
        if (assignment is null)
        {
            return false;
        }

        ProcessorRegistration? registration = _registry.Find(assignment.JobType, assignment.ProcessorKey);
        if (registration is null)
        {
            await _store.DeadLetterAssignmentAsync(
                assignment.Id,
                _owner,
                $"No processor registered for job type '{assignment.JobType}' with key '{assignment.ProcessorKey}'.",
                CancellationToken.None);
            return true;
        }

        await ExecuteAssignmentAsync(assignment, registration, cancellationToken);
        return true;
    }

    private async Task ExecuteAssignmentAsync(AssignmentRecord assignment, ProcessorRegistration registration, CancellationToken cancellationToken)
    {
        IJobContext context = new JobContext(assignment.JobId, assignment.Id, assignment.ProcessorKey, assignment.AttemptCount);
        try
        {
            object payload = _serializer.Deserialize(assignment.Payload, registration.PayloadType);
            using IServiceScope scope = _scopeFactory.CreateScope();
            await RunWithHeartbeatAsync(assignment, registration, scope.ServiceProvider, payload, context, cancellationToken);

            await _store.CompleteAssignmentAsync(assignment.Id, _owner, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The worker is shutting down (drain elapsed). Put the assignment back to run promptly
            // on the next start rather than recording it as a failed attempt against the policy.
            await _store.RescheduleAssignmentAsync(
                assignment.Id,
                _owner,
                _timeProvider.GetUtcNow(),
                "Worker stopped before the assignment completed.",
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            await HandleFailureAsync(assignment, exception);
        }
    }

    private async Task RunWithHeartbeatAsync(
        AssignmentRecord assignment,
        ProcessorRegistration registration,
        IServiceProvider services,
        object payload,
        IJobContext context,
        CancellationToken cancellationToken)
    {
        TimeSpan interval = _options.LeaseDuration > TimeSpan.Zero
            ? TimeSpan.FromTicks(_options.LeaseDuration.Ticks / 2)
            : TimeSpan.Zero;

        Task work = registration.Invoke(services, payload, context, cancellationToken);

        while (interval > TimeSpan.Zero && !work.IsCompleted)
        {
            // The timer uses an uncancelled token; cancellation reaches the processor through the
            // token passed to Invoke, and we observe the result by awaiting work below.
            Task timer = Task.Delay(interval, _timeProvider, CancellationToken.None);
            await Task.WhenAny(work, timer);
            if (work.IsCompleted || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            bool renewed = await _store.HeartbeatAssignmentAsync(
                assignment.Id,
                _owner,
                _timeProvider.GetUtcNow() + _options.LeaseDuration,
                CancellationToken.None);
            if (!renewed)
            {
                // Lost the lease (for example reclaimed elsewhere). Stop heartbeating; the result
                // of work is still observed below, but a completion call may no longer be ours.
                break;
            }
        }

        await work;
    }

    private async Task HandleFailureAsync(AssignmentRecord assignment, Exception exception)
    {
        string error = exception.ToString();
        if (assignment.AttemptCount >= _options.MaxAttempts)
        {
            await _store.DeadLetterAssignmentAsync(assignment.Id, _owner, error, CancellationToken.None);
        }
        else
        {
            DateTimeOffset nextRunAt = _timeProvider.GetUtcNow() + ComputeBackoff(assignment.AttemptCount);
            await _store.RescheduleAssignmentAsync(assignment.Id, _owner, nextRunAt, error, CancellationToken.None);
        }
    }

    private TimeSpan ComputeBackoff(int attemptCount)
    {
        double factor = Math.Pow(2, Math.Max(0, attemptCount - 1));
        double milliseconds = _options.BaseRetryDelay.TotalMilliseconds * factor;
        double capped = Math.Min(milliseconds, _options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }
}
