using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LasseVK.JobSystem.Execution;

/// <summary>
/// The hosted service that drives the job system: each tick it reclaims work left by crashed
/// workers, dispatches one pending job, and executes one due assignment. When there is nothing to
/// do it idles for <see cref="JobSystemOptions.PollInterval"/>. On shutdown it stops claiming new
/// work and lets an in-flight assignment run for up to <see cref="JobSystemOptions.DrainTimeout"/>
/// before its cancellation token is signalled.
/// </summary>
public sealed class JobSystemWorker : BackgroundService
{
    private readonly JobRunner _runner;
    private readonly JobSystemOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobSystemWorker> _logger;

    public JobSystemWorker(JobRunner runner, JobSystemOptions options, TimeProvider timeProvider, ILogger<JobSystemWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource executionCts = new();
        using CancellationTokenRegistration drain = stoppingToken.Register(static state =>
        {
            (CancellationTokenSource cts, TimeSpan drainTimeout) = ((CancellationTokenSource, TimeSpan))state!;
            try
            {
                cts.CancelAfter(drainTimeout);
            }
            catch (ObjectDisposedException)
            {
                // The worker already finished; nothing to drain.
            }
        }, (executionCts, _options.DrainTimeout));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runner.ReclaimExpiredAsync(stoppingToken);
                bool dispatched = await _runner.TryDispatchAsync(stoppingToken);
                bool executed = await _runner.TryExecuteAsync(executionCts.Token);
                if (!dispatched && !executed)
                {
                    await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Job system worker iteration failed; backing off before retrying.");
                try
                {
                    await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
