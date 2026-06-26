using System.Diagnostics.CodeAnalysis;

namespace LasseVK.JobSystem.Storage;

/// <summary>
/// An in-memory <see cref="IJobStore"/> for tests and small programs. All operations are
/// serialized under a single lock, which trivially satisfies the atomic-claim contract.
/// </summary>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, JobRecord> _jobs = new();
    private readonly Dictionary<Guid, AssignmentRecord> _assignments = new();
    private readonly Dictionary<Guid, long> _sequence = new();
    private long _nextSequence;

    public Task InsertJobAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_jobs.ContainsKey(job.Id))
            {
                throw new InvalidOperationException($"A job with id '{job.Id}' already exists.");
            }

            _jobs[job.Id] = job;
            _sequence[job.Id] = _nextSequence++;
        }

        return Task.CompletedTask;
    }

    public Task<JobRecord?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_jobs.GetValueOrDefault(jobId));
        }
    }

    public Task<JobRecord?> ClaimNextJobAsync(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            JobRecord? next = _jobs.Values
                .Where(j => j.Status == JobStatus.Pending && (j.LeaseExpiresAt is null || j.LeaseExpiresAt <= now))
                .OrderBy(j => _sequence[j.Id])
                .FirstOrDefault();

            if (next is null)
            {
                return Task.FromResult<JobRecord?>(null);
            }

            JobRecord claimed = next with { LeaseOwner = leaseOwner, LeaseExpiresAt = leaseExpiresAt };
            _jobs[claimed.Id] = claimed;
            return Task.FromResult<JobRecord?>(claimed);
        }
    }

    public Task<bool> ExpandJobAsync(Guid jobId, string leaseOwner, IReadOnlyCollection<AssignmentRecord> assignments, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out JobRecord? job))
            {
                return Task.FromResult(false);
            }

            if (job.Status != JobStatus.Pending || job.LeaseOwner != leaseOwner)
            {
                return Task.FromResult(false);
            }

            foreach (AssignmentRecord assignment in assignments)
            {
                _assignments[assignment.Id] = assignment;
                if (!_sequence.ContainsKey(assignment.Id))
                {
                    _sequence[assignment.Id] = _nextSequence++;
                }
            }

            JobStatus newStatus = assignments.Count > 0 ? JobStatus.Assigned : JobStatus.Unhandled;
            _jobs[jobId] = job with { Status = newStatus, LeaseOwner = null, LeaseExpiresAt = null };
            return Task.FromResult(true);
        }
    }

    public Task<AssignmentRecord?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_assignments.GetValueOrDefault(assignmentId));
        }
    }

    public Task<AssignmentRecord?> ClaimNextAssignmentAsync(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            AssignmentRecord? next = _assignments.Values
                .Where(a => a.Status == AssignmentStatus.Pending
                            && a.NextRunAt <= now
                            && (a.LeaseExpiresAt is null || a.LeaseExpiresAt <= now))
                .OrderBy(a => a.NextRunAt)
                .ThenBy(a => _sequence[a.Id])
                .FirstOrDefault();

            if (next is null)
            {
                return Task.FromResult<AssignmentRecord?>(null);
            }

            AssignmentRecord claimed = next with
            {
                Status = AssignmentStatus.Running,
                AttemptCount = next.AttemptCount + 1,
                LeaseOwner = leaseOwner,
                LeaseExpiresAt = leaseExpiresAt
            };
            _assignments[claimed.Id] = claimed;
            return Task.FromResult<AssignmentRecord?>(claimed);
        }
    }

    public Task<bool> HeartbeatAssignmentAsync(Guid assignmentId, string leaseOwner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!TryGetOwnedRunning(assignmentId, leaseOwner, out AssignmentRecord? assignment))
            {
                return Task.FromResult(false);
            }

            _assignments[assignmentId] = assignment with { LeaseExpiresAt = leaseExpiresAt };
            return Task.FromResult(true);
        }
    }

    public Task<bool> CompleteAssignmentAsync(Guid assignmentId, string leaseOwner, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!TryGetOwnedRunning(assignmentId, leaseOwner, out AssignmentRecord? assignment))
            {
                return Task.FromResult(false);
            }

            _assignments[assignmentId] = assignment with
            {
                Status = AssignmentStatus.Succeeded,
                LeaseOwner = null,
                LeaseExpiresAt = null
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> RescheduleAssignmentAsync(Guid assignmentId, string leaseOwner, DateTimeOffset nextRunAt, string? lastError, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!TryGetOwnedRunning(assignmentId, leaseOwner, out AssignmentRecord? assignment))
            {
                return Task.FromResult(false);
            }

            _assignments[assignmentId] = assignment with
            {
                Status = AssignmentStatus.Pending,
                NextRunAt = nextRunAt,
                LastError = lastError,
                LeaseOwner = null,
                LeaseExpiresAt = null
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeadLetterAssignmentAsync(Guid assignmentId, string leaseOwner, string? lastError, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!TryGetOwnedRunning(assignmentId, leaseOwner, out AssignmentRecord? assignment))
            {
                return Task.FromResult(false);
            }

            _assignments[assignmentId] = assignment with
            {
                Status = AssignmentStatus.DeadLettered,
                LastError = lastError,
                LeaseOwner = null,
                LeaseExpiresAt = null
            };
            return Task.FromResult(true);
        }
    }

    public Task<int> ReclaimExpiredAssignmentsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            List<AssignmentRecord> expired = _assignments.Values
                .Where(a => a.Status == AssignmentStatus.Running && a.LeaseExpiresAt is not null && a.LeaseExpiresAt <= now)
                .ToList();

            foreach (AssignmentRecord assignment in expired)
            {
                _assignments[assignment.Id] = assignment with
                {
                    Status = AssignmentStatus.Pending,
                    LeaseOwner = null,
                    LeaseExpiresAt = null
                };
            }

            return Task.FromResult(expired.Count);
        }
    }

    private bool TryGetOwnedRunning(Guid assignmentId, string leaseOwner, [NotNullWhen(true)] out AssignmentRecord? assignment)
    {
        if (_assignments.TryGetValue(assignmentId, out assignment)
            && assignment.Status == AssignmentStatus.Running
            && assignment.LeaseOwner == leaseOwner)
        {
            return true;
        }

        assignment = null;
        return false;
    }
}
