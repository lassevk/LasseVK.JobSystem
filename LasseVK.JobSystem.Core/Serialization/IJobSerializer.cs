namespace LasseVK.JobSystem.Serialization;

/// <summary>
/// Serializes job payloads to and from their stored string form. Pluggable so consumers can
/// swap the default JSON implementation.
/// </summary>
public interface IJobSerializer
{
    /// <summary>Serializes <paramref name="payload"/> as the given <paramref name="payloadType"/>.</summary>
    string Serialize(object payload, Type payloadType);

    /// <summary>Deserializes <paramref name="payload"/> back into an instance of <paramref name="payloadType"/>.</summary>
    object Deserialize(string payload, Type payloadType);
}
