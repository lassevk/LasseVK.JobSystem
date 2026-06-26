namespace LasseVK.JobSystem.Execution;

/// <summary>
/// The set of registered processors, built once at registration time. The dispatch step looks up
/// all processors for a job type to fan it out into assignments; the execution step finds the one
/// processor for a specific (job type, processor key) pair to run an assignment.
/// </summary>
public sealed class ProcessorRegistry
{
    private readonly Dictionary<string, List<ProcessorRegistration>> _byJobType = new();
    private readonly Dictionary<(string JobType, string ProcessorKey), ProcessorRegistration> _byKey = new();

    /// <summary>
    /// Adds a registration, rejecting a duplicate (job type, processor key) pair so configuration
    /// problems surface at startup rather than at run time.
    /// </summary>
    public void Add(ProcessorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        (string JobType, string ProcessorKey) key = (registration.JobType, registration.ProcessorKey);
        if (!_byKey.TryAdd(key, registration))
        {
            throw new InvalidOperationException(
                $"A processor for job type '{registration.JobType}' with key '{registration.ProcessorKey}' is already registered.");
        }

        if (!_byJobType.TryGetValue(registration.JobType, out List<ProcessorRegistration>? list))
        {
            list = new List<ProcessorRegistration>();
            _byJobType[registration.JobType] = list;
        }

        list.Add(registration);
    }

    /// <summary>Returns all processors registered for a job type, or an empty list if none are.</summary>
    public IReadOnlyList<ProcessorRegistration> ForJobType(string jobType)
    {
        return _byJobType.TryGetValue(jobType, out List<ProcessorRegistration>? list)
            ? list
            : Array.Empty<ProcessorRegistration>();
    }

    /// <summary>Finds the single processor for a (job type, processor key) pair, or <c>null</c> if none is registered.</summary>
    public ProcessorRegistration? Find(string jobType, string processorKey)
    {
        return _byKey.GetValueOrDefault((jobType, processorKey));
    }
}
