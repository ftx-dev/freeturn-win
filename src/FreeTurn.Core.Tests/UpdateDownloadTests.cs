using System.Net;
using Xunit;

namespace FreeTurn.Core.Tests;

public class UpdateDownloadTests
{
    sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response());
    }
    sealed class ProgressSink : IProgress<DownloadProgress>
    {
        public DownloadProgress Last;
        public void Report(DownloadProgress value) => Last = value;
    }
    sealed class Stalled : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
    }

    [Fact]
    public async Task DownloadsAndReportsActualBytes()
    {
        var data = new byte[150000];
        Random.Shared.NextBytes(data);
        using var http = new HttpClient(new Handler(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
        var path = Path.GetTempFileName();
        var progress = new ProgressSink();
        try
        {
            await UpdateDownload.DownloadAsync(http, new Uri("https://example.test/update"), path, progress, default);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.Equal(data.Length, progress.Last.Received);
            Assert.Equal(data.Length, progress.Last.Total);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StalledBodyTimesOutAndDeletesPartialFile()
    {
        using var http = new HttpClient(new Handler(() => new(HttpStatusCode.OK) { Content = new StreamContent(new Stalled()) }));
        var path = Path.GetTempFileName();
        await Assert.ThrowsAsync<TimeoutException>(() => UpdateDownload.DownloadAsync(http,
            new Uri("https://example.test/update"), path, null, default, TimeSpan.FromMilliseconds(80)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CancellationKeepsItsMeaningAndDeletesPartialFile()
    {
        using var http = new HttpClient(new Handler(() => new(HttpStatusCode.OK) { Content = new StreamContent(new Stalled()) }));
        var path = Path.GetTempFileName();
        using var cts = new CancellationTokenSource(80);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateDownload.DownloadAsync(http,
            new Uri("https://example.test/update"), path, null, cts.Token));
        Assert.False(File.Exists(path));
    }
}
