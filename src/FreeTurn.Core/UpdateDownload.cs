namespace FreeTurn.Core;

public readonly record struct DownloadProgress(long Received, long? Total);

public static class UpdateDownload
{
    public static async Task DownloadAsync(HttpClient http, Uri url, string target,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken,
        TimeSpan? idleTimeout = null)
    {
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(TimeSpan.FromMinutes(15));
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
        var timeout = idleTimeout ?? TimeSpan.FromSeconds(30);
        try
        {
            idle.CancelAfter(timeout);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(idle.Token);
            await using var output = File.Create(target);
            var buffer = new byte[65536];
            long received = 0;
            var lastReport = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report(new(received, total));
            while (true)
            {
                idle.CancelAfter(timeout);
                var count = await input.ReadAsync(buffer, idle.Token);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), idle.Token);
                received += count;
                if (lastReport.ElapsedMilliseconds >= 100)
                {
                    progress?.Report(new(received, total));
                    lastReport.Restart();
                }
            }
            if (total is { } expected && received != expected) throw new IOException("Incomplete update download");
            progress?.Report(new(received, total));
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { }
            if (!cancellationToken.IsCancellationRequested && idle.IsCancellationRequested)
                throw new TimeoutException("Update download stalled");
            throw;
        }
    }
}
