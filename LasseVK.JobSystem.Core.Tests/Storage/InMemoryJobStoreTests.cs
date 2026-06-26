using LasseVK.JobSystem.Storage;

namespace LasseVK.JobSystem.Tests.Storage;

/// <summary>Runs the <see cref="JobStoreConformanceTests"/> suite against <see cref="InMemoryJobStore"/>.</summary>
public sealed class InMemoryJobStoreTests : JobStoreConformanceTests
{
    protected override IJobStore CreateStore()
    {
        return new InMemoryJobStore();
    }
}
