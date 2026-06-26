using LasseVK.JobSystem.Serialization;

namespace LasseVK.JobSystem.Tests.Serialization;

public class JsonJobSerializerTests
{
    [Fact]
    public void Round_trips_a_payload()
    {
        IJobSerializer serializer = new JsonJobSerializer();

        string serialized = serializer.Serialize(new SampleJob { Message = "hi" }, typeof(SampleJob));
        SampleJob roundTrip = (SampleJob)serializer.Deserialize(serialized, typeof(SampleJob));

        Assert.Equal("hi", roundTrip.Message);
    }
}
