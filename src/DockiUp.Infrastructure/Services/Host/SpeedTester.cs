using System.Diagnostics;
using DockiUp.Application.Dtos;

namespace DockiUp.Infrastructure.Services.Host
{
    /// <summary>Measures the host's internet connection against Cloudflare's speed test endpoints (the ones
    /// speed.cloudflare.com uses): latency from small requests, then a few parallel streams per direction for a few
    /// seconds each. Short and capped, so a test moves at most a few hundred MB even on a fast line.</summary>
    public static class SpeedTester
    {
        public const string Server = "speed.cloudflare.com";
        private static readonly HttpClient Http = new() { BaseAddress = new Uri($"https://{Server}/"), Timeout = TimeSpan.FromSeconds(30) };
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(6);
        private const int Streams = 4;

        public static async Task<SpeedTestDto> RunAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (ping, jitter) = await LatencyAsync(cancellationToken);
                var down = await ThroughputAsync(async (token, count) =>
                {
                    using var response = await Http.GetAsync("__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await stream.ReadAsync(buffer, token)) > 0) count(n);
                }, cancellationToken);
                var payload = new byte[1_000_000];
                var up = await ThroughputAsync(async (token, count) =>
                {
                    using var response = await Http.PostAsync("__up", new ByteArrayContent(payload), token);
                    response.EnsureSuccessStatusCode();
                    count(payload.Length);
                }, cancellationToken);
                return new SpeedTestDto(null, DateTime.UtcNow, Math.Round(down, 1), Math.Round(up, 1), Math.Round(ping, 1), Math.Round(jitter, 1), Server, null);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new SpeedTestDto(null, DateTime.UtcNow, null, null, null, null, Server, e is OperationCanceledException ? "Timed out" : e.Message);
            }
        }

        /// <summary>Median round trip of empty downloads (after one warm-up for DNS and TLS), and the average change between them.</summary>
        private static async Task<(double Ping, double Jitter)> LatencyAsync(CancellationToken cancellationToken)
        {
            var times = new List<double>();
            for (var i = 0; i < 9; i++)
            {
                var sw = Stopwatch.StartNew();
                using var response = await Http.GetAsync("__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                sw.Stop();
                if (i > 0) times.Add(sw.Elapsed.TotalMilliseconds);
            }
            var sorted = times.Order().ToList();
            var jitter = times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average();
            return (sorted[sorted.Count / 2], jitter);
        }

        /// <summary>Mbit/s over <see cref="Window"/> with <see cref="Streams"/> transfers going at once.</summary>
        private static async Task<double> ThroughputAsync(Func<CancellationToken, Action<int>, Task> transfer, CancellationToken cancellationToken)
        {
            long bytes = 0;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stop.CancelAfter(Window);
            var sw = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, Streams).Select(async _ =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                        await transfer(stop.Token, n => Interlocked.Add(ref bytes, n));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { } // the window closed mid-transfer
            }));
            return Interlocked.Read(ref bytes) * 8 / sw.Elapsed.TotalSeconds / 1_000_000;
        }
    }
}
