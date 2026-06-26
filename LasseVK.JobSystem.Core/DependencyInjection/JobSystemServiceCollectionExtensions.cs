using LasseVK.JobSystem.Execution;
using LasseVK.JobSystem.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LasseVK.JobSystem.DependencyInjection;

/// <summary>Registration entry point for the job system.</summary>
public static class JobSystemServiceCollectionExtensions
{
    /// <summary>
    /// Registers the job system: the submission API (<see cref="IJobQueue"/>), the default JSON
    /// serializer, the processor registry, and the background worker. Configure the store and
    /// processors on the returned builder. A submit-only client can register this without any
    /// processors; a worker process also calls <c>AddProcessor</c> for each handler.
    /// </summary>
    public static JobSystemBuilder AddJobSystem(this IServiceCollection services, Action<JobSystemOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        JobSystemOptions options = new();
        configure?.Invoke(options);
        services.AddSingleton(options);

        ProcessorRegistry registry = new();
        services.AddSingleton(registry);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJobSerializer, JsonJobSerializer>();
        services.TryAddSingleton<IJobQueue, JobQueue>();
        services.TryAddSingleton<JobRunner>();
        services.AddHostedService<JobSystemWorker>();

        return new JobSystemBuilder(services, registry);
    }
}
