using System.Diagnostics;
using System.Net;
using DockiUp.Application.Dtos;

namespace DockiUp.Infrastructure.Services.Host
{
    /// <summary>Measures the host's internet connection against Cloudflare's speed test endpoints (the ones
    /// speed.cloudflare.com uses): latency from small requests, then a few parallel streams per direction for a few
    /// seconds each, stopping early after <see cref="MaxBytes"/>. Cloudflare throttles (429) hosts that send many
    /// requests, and on a multi-gigabit line even 25 MB ones finish in a blink: the byte cap keeps a test to about ten
    /// requests per direction (and bounds its data use), while still measuring for most of a second.</summary>
    public static class SpeedTester
    {
        public const string Server = "speed.cloudflare.com";
        private static readonly HttpClient Http = new() { BaseAddress = new Uri($"https://{Server}/"), Timeout = TimeSpan.FromSeconds(30) };
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(6);
        private const int Streams = 4;
        // The largest single transfer Cloudflare's endpoints accept (bigger downloads get a 403).
        private const int TransferBytes = 25_000_000;
        private const long MaxBytes = 250_000_000;

        public static async Task<SpeedTestDto> RunAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (ping, jitter) = await LatencyAsync(cancellationToken);
                var down = await ThroughputAsync(async (token, count) =>
                {
                    using var response = await Http.GetAsync($"__down?bytes={TransferBytes}", HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await stream.ReadAsync(buffer, token)) > 0) count(n);
                }, cancellationToken);
                var up = await ThroughputAsync(async (token, count) =>
                {
                    using var response = await Http.PostAsync("__up", new CountingContent(TransferBytes, count), token);
                    response.EnsureSuccessStatusCode();
                }, cancellationToken);
                return new SpeedTestDto(null, DateTime.UtcNow, Math.Round(down, 1), Math.Round(up, 1), Math.Round(ping, 1), Math.Round(jitter, 1), Server, null);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var error = e switch
                {
                    HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Cloudflare is limiting tests from this host for now; it tries again in an hour.",
                    OperationCanceledException => "Timed out",
                    _ => e.Message,
                };
                return new SpeedTestDto(null, DateTime.UtcNow, null, null, null, null, Server, error);
            }
        }

        /// <summary>Median round trip of empty downloads (after one warm-up for DNS and TLS), and the average change between them.</summary>
        private static async Task<(double Ping, double Jitter)> LatencyAsync(CancellationToken cancellationToken)
        {
            var times = new List<double>();
            for (var i = 0; i < 6; i++)
            {
                var sw = Stopwatch.StartNew();
                using var response = await Http.GetAsync("__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                sw.Stop();
                response.EnsureSuccessStatusCode();
                if (i > 0) times.Add(sw.Elapsed.TotalMilliseconds);
            }
            var sorted = times.Order().ToList();
            var jitter = times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average();
            return (sorted[sorted.Count / 2], jitter);
        }

        /// <summary>Mbit/s with <see cref="Streams"/> transfers going at once, over <see cref="Window"/> or until
        /// <see cref="MaxBytes"/> moved, whichever comes first.</summary>
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
                        await transfer(stop.Token, n =>
                        {
                            if (Interlocked.Add(ref bytes, n) >= MaxBytes) stop.Cancel();
                        });
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { } // the window closed mid-transfer
            }));
            return Interlocked.Read(ref bytes) * 8 / sw.Elapsed.TotalSeconds / 1_000_000;
        }

        /// <summary>An upload body of zeros that reports each chunk as it is sent, so a transfer cut off by the window still counts.</summary>
        private sealed class CountingContent(int length, Action<int> sent) : HttpContent
        {
            private static readonly byte[] Chunk = new byte[65536];

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                for (var left = length; left > 0; left -= Chunk.Length)
                {
                    var n = Math.Min(left, Chunk.Length);
                    await stream.WriteAsync(Chunk.AsMemory(0, n), cancellationToken);
                    sent(n);
                }
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

            protected override bool TryComputeLength(out long size)
            {
                size = length;
                return true;
            }
        }
    }
}
