using System.Security.Cryptography;
using System.Text;

namespace LasseVK.JobSystem.Execution;

/// <summary>
/// Derives a stable assignment id from a job id and processor key. Re-running the dispatch step
/// for the same job (for example after a crash, before the status flip committed) produces the
/// same ids, so the store's idempotent upsert avoids duplicate assignments.
/// </summary>
internal static class DeterministicGuid
{
    public static Guid Create(Guid jobId, string processorKey)
    {
        byte[] jobBytes = jobId.ToByteArray();
        byte[] keyBytes = Encoding.UTF8.GetBytes(processorKey);

        byte[] buffer = new byte[jobBytes.Length + keyBytes.Length];
        Buffer.BlockCopy(jobBytes, 0, buffer, 0, jobBytes.Length);
        Buffer.BlockCopy(keyBytes, 0, buffer, jobBytes.Length, keyBytes.Length);

        byte[] hash = SHA256.HashData(buffer);
        return new Guid(hash.AsSpan(0, 16));
    }
}
