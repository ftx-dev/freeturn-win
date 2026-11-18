using Xunit;

namespace FreeTurn.Core.Tests;

public class StreamLimitsTests
{
    [Theory]
    [InlineData(false, 96, 48, 24, 24)]
    [InlineData(true, 120, 90, 96, 48)]
    [InlineData(false, 12, 6, 12, 6)]
    [InlineData(true, 0, -1, 1, 1)]
    public void ClampsWithoutIncreasingSavedValues(bool extended, int total, int perUser, int expectedTotal, int expectedPerUser)
    {
        var client = new ClientConfig { Threads = total, StreamsPerCred = perUser };
        StreamLimits.Apply(client, extended);
        Assert.Equal(expectedTotal, client.Threads);
        Assert.Equal(expectedPerUser, client.StreamsPerCred);
    }
}
