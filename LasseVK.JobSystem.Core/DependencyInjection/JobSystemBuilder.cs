using System.Reflection;
using LasseVK.JobSystem.Execution;
using LasseVK.JobSystem.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LasseVK.JobSystem.DependencyInjection;

/// <summary>
/// Configures the job system on an <see cref="IServiceCollection"/>: which store to use and which
/// processors to register. Returned by <c>AddJobSystem</c>.
/// </summary>
public sealed class JobSystemBuilder
{
    private static readonly MethodInfo _createRegistrationMethod =
        typeof(JobSystemBuilder).GetMethod(nameof(CreateRegistration), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly ProcessorRegistry _registry;

    internal JobSystemBuilder(IServiceCollection services, ProcessorRegistry registry)
    {
        Services = services;
        _registry = registry;
    }

    /// <summary>The underlying service collection, for advanced registration.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Uses the in-process <see cref="InMemoryJobStore"/> (for tests and small programs).</summary>
    public JobSystemBuilder UseInMemoryStore()
    {
        Services.AddSingleton<IJobStore, InMemoryJobStore>();
        return this;
    }

    /// <summary>
    /// Registers a processor. Reads its <c>ProcessorKey</c> and the job type it serves, registers
    /// it as a keyed service under that key, and records it in the processor registry. Throws if
    /// the same (job type, processor key) pair is registered twice, or if the processor implements
    /// more than one <see cref="IJobProcessor{T}"/> (a processor must handle exactly one payload type).
    /// </summary>
    /// <typeparam name="TProcessor">A concrete <see cref="IJobProcessor{T}"/> implementation.</typeparam>
    public JobSystemBuilder AddProcessor<TProcessor>() where TProcessor : class, IJobProcessor
    {
        Type processorType = typeof(TProcessor);
        Type[] closedInterfaces = processorType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJobProcessor<>))
            .ToArray();
        if (closedInterfaces.Length == 0)
        {
            throw new InvalidOperationException($"Processor '{processorType}' does not implement IJobProcessor<T>.");
        }

        if (closedInterfaces.Length > 1)
        {
            string payloadTypes = string.Join(", ", closedInterfaces.Select(i => i.GetGenericArguments()[0].Name));
            throw new InvalidOperationException(
                $"Processor '{processorType}' implements IJobProcessor<T> for more than one payload type ({payloadTypes}). A processor must handle exactly one payload type.");
        }

        Type closedInterface = closedInterfaces[0];
        Type payloadType = closedInterface.GetGenericArguments()[0];
        string processorKey = TProcessor.ProcessorKey;

        Services.AddKeyedScoped(closedInterface, processorKey, processorType);

        var registration =
            (ProcessorRegistration)_createRegistrationMethod.MakeGenericMethod(payloadType).Invoke(null, [processorKey])!;
        _registry.Add(registration);

        return this;
    }

    private static ProcessorRegistration CreateRegistration<T>(string processorKey) where T : IJob
    {
        return new ProcessorRegistration
        {
            JobType = T.JobType,
            ProcessorKey = processorKey,
            PayloadType = typeof(T),
            Invoke = (services, payload, context, cancellationToken) =>
                ((IJobProcessor<T>)services.GetRequiredKeyedService(typeof(IJobProcessor<T>), processorKey))
                    .ProcessAsync((T)payload, context, cancellationToken),
        };
    }
}