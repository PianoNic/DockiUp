using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using Microsoft.Extensions.Options;

namespace DockiUp.Infrastructure.Services
{
    // Bridges a container's TTY over a hand-rolled hijacked Docker stream (npipe/unix upgrade dance).
    // Only meaningful against a real daemon + container; covered by the web-terminal integration tests.
    [ExcludeFromCodeCoverage]
    public sealed class ContainerExecRegistry : IContainerExecRegistry
    {
        private readonly IDockerClient _docker;
        private readonly string? _endpointOverride;
        private readonly ConcurrentDictionary<string, ContainerExecSession> _sessions = new();

        public ContainerExecRegistry(IDockiUpDockerClient client, IOptions<SystemPaths> systemPaths)
        {
            _docker = client.DockerClient;
            _endpointOverride = systemPaths.Value.DockerSocket;
        }

        private static async Task<IList<string>> PickShellCmdAsync(IDockerClient docker, string containerId, CancellationToken ct)
        {
            try
            {
                var exec = await docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    Cmd = new[] { "sh", "-c", "command -v bash" },
                }, ct);
                using var stream = await docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);
                using var ms = new MemoryStream();
                await stream.CopyOutputToAsync(null, ms, null, ct);
                var path = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                if (!string.IsNullOrEmpty(path)) return new[] { path };
            }
            catch { /* fall through to sh */ }
            return new[] { "/bin/sh" };
        }

        public async Task<IContainerExecSession> StartAsync(string containerId, uint cols, uint rows, CancellationToken cancellationToken = default)
        {
            var cmd = await PickShellCmdAsync(_docker, containerId, cancellationToken);

            // Docker.DotNet's hijacked-stream helper omits the Upgrade/Connection headers, so the
            // daemon never switches to raw bidi mode and silently drops stdin. We use Docker.DotNet for
            // exec CREATE (a normal POST), then open our own connection for /exec/{id}/start so we can
            // speak the upgrade dance ourselves.
            var exec = await _docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                Tty = true,
                Cmd = cmd,
                Env = new[] { "TERM=xterm-256color" },
            }, cancellationToken);

            var transport = await OpenTransportAsync(cancellationToken);
            await SendExecStartUpgradeAsync(transport, exec.ID, cancellationToken);

            try
            {
                await _docker.Exec.ResizeContainerExecTtyAsync(exec.ID, new ContainerResizeParameters { Height = rows, Width = cols }, cancellationToken);
            }
            catch
            {
                // Resize fails on some daemons before any output is produced; the client retries
                // via the fit-addon once xterm.fit() has had a layout pass.
            }

            var session = new ContainerExecSession(exec.ID, transport, _docker);
            _sessions[session.Id] = session;
            session.Start();
            session.Exited += async code =>
            {
                _sessions.TryRemove(session.Id, out _);
                await Task.CompletedTask;
            };
            return session;
        }

        public IContainerExecSession? Get(string sessionId) => _sessions.TryGetValue(sessionId, out var s) ? s : null;

        public async Task EndAsync(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                await session.DisposeAsync();
            }
        }

        private async Task<Stream> OpenTransportAsync(CancellationToken ct)
        {
            // Resolve docker daemon transport: explicit override (npipe://, unix://), else platform
            // default. Works on Windows for dev and inside a Linux container for prod (socket mounted).
            var endpoint = _endpointOverride;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
            }

            if (endpoint.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
            {
                var path = endpoint["npipe://".Length..];
                var (server, pipe) = ParseNpipe(path);
                var stream = new NamedPipeClientStream(server, pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                await stream.ConnectAsync(5000, ct);
                return stream;
            }

            if (endpoint.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
            {
                var path = endpoint["unix://".Length..];
                var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await sock.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);
                return new NetworkStream(sock, ownsSocket: true);
            }

            throw new NotSupportedException($"Docker endpoint scheme not supported for interactive exec: {endpoint}");
        }

        private static (string Server, string Pipe) ParseNpipe(string path)
        {
            var trimmed = path.TrimStart('/', '.');
            var idx = trimmed.IndexOf("pipe/", StringComparison.Ordinal);
            var pipeName = idx >= 0 ? trimmed[(idx + "pipe/".Length)..] : trimmed;
            return (".", pipeName);
        }

        private static async Task SendExecStartUpgradeAsync(Stream transport, string execId, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes("{\"Detach\":false,\"Tty\":true}");
            var header = new StringBuilder();
            header.Append($"POST /v1.41/exec/{execId}/start HTTP/1.1\r\n");
            header.Append("Host: docker\r\n");
            header.Append("Content-Type: application/json\r\n");
            header.Append($"Content-Length: {body.Length}\r\n");
            header.Append("Upgrade: tcp\r\n");
            header.Append("Connection: Upgrade\r\n");
            header.Append("\r\n");
            var headerBytes = Encoding.UTF8.GetBytes(header.ToString());
            await transport.WriteAsync(headerBytes, ct);
            await transport.WriteAsync(body, ct);
            await transport.FlushAsync(ct);

            // Consume response headers up to the blank line. After that, bytes are raw bidi.
            var status = await ReadLineAsync(transport, ct);
            if (!status.StartsWith("HTTP/1.1 101", StringComparison.Ordinal) && !status.StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Docker exec start did not upgrade: {status}");
            }
            while (true)
            {
                var line = await ReadLineAsync(transport, ct);
                if (string.IsNullOrEmpty(line)) break;
            }
        }

        private static async Task<string> ReadLineAsync(Stream s, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var prev = 0;
            var single = new byte[1];
            while (true)
            {
                var n = await s.ReadAsync(single, ct);
                if (n == 0) return sb.ToString();
                var b = single[0];
                if (prev == '\r' && b == '\n') { sb.Length -= 1; return sb.ToString(); }
                sb.Append((char)b);
                prev = b;
            }
        }
    }

    [ExcludeFromCodeCoverage]
    internal sealed class ContainerExecSession(string id, Stream transport, IDockerClient docker) : IContainerExecSession
    {
        public string Id { get; } = id;
        public event Func<ReadOnlyMemory<byte>, Task>? Output;
        public event Func<long?, Task>? Exited;

        private readonly CancellationTokenSource _cts = new();
        private Task? _pump;
        private int _disposed;

        public void Start()
        {
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var n = await transport.ReadAsync(buffer.AsMemory(), _cts.Token);
                    if (n == 0) break;
                    var slice = new byte[n];
                    Buffer.BlockCopy(buffer, 0, slice, 0, n);
                    if (Output is { } cb)
                    {
                        try { await cb(slice); } catch { /* subscriber gone */ }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                long? exitCode = null;
                try
                {
                    var inspect = await docker.Exec.InspectContainerExecAsync(Id, CancellationToken.None);
                    exitCode = inspect.ExitCode;
                }
                catch { /* container gone */ }
                if (Exited is { } cb)
                {
                    try { await cb(exitCode); } catch { /* subscriber gone */ }
                }
            }
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (_disposed != 0) return;
            await transport.WriteAsync(data, cancellationToken);
            await transport.FlushAsync(cancellationToken);
        }

        public Task ResizeAsync(uint cols, uint rows, CancellationToken cancellationToken = default)
        {
            return docker.Exec.ResizeContainerExecTtyAsync(Id, new ContainerResizeParameters { Height = rows, Width = cols }, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Cancel(); } catch { }
            try { transport.Dispose(); } catch { }
            if (_pump is not null)
            {
                try { await _pump; } catch { }
            }
            _cts.Dispose();
        }
    }
}
