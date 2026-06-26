using System.Text.Json;

namespace LasseVK.JobSystem.Serialization;

/// <summary>
/// The default <see cref="IJobSerializer"/>, backed by <see cref="System.Text.Json"/>.
/// </summary>
public sealed class JsonJobSerializer : IJobSerializer
{
    private readonly JsonSerializerOptions? _options;

    /// <param name="options">Optional serializer options; the System.Text.Json defaults are used when null.</param>
    public JsonJobSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    public string Serialize(object payload, Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payloadType);

        return JsonSerializer.Serialize(payload, payloadType, _options);
    }

    public object Deserialize(string payload, Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payloadType);

        object? result = JsonSerializer.Deserialize(payload, payloadType, _options);
        if (result is null)
        {
            throw new InvalidOperationException($"Deserialization of payload type '{payloadType}' produced null.");
        }

        return result;
    }
}
