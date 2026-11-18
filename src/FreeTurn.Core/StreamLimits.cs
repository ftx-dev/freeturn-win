namespace FreeTurn.Core;

public static class StreamLimits
{
    public static void Apply(ClientConfig client, bool extended)
    {
        client.Threads = Math.Clamp(client.Threads, 1, extended ? 96 : 24);
        client.StreamsPerCred = Math.Clamp(client.StreamsPerCred, 1, extended ? 48 : 24);
    }
}
