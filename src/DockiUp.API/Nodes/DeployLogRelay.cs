using System.Collections.Concurrent;

namespace DockiUp.API.Nodes
{
    /// <summary>Routes deploy-log lines a node streams back (NodeHub.DeployLog) to the server-side call
    /// that started that node operation, keyed by a per-call run id.</summary>
    public sealed class DeployLogRelay
    {
        private readonly ConcurrentDictionary<string, Func<string, Task>> _runs = new();

        public string Register(Func<string, Task> log)
        {
            var runId = Guid.NewGuid().ToString("N");
            _runs[runId] = log;
            return runId;
        }

        public Task WriteAsync(string runId, string line)
            => _runs.TryGetValue(runId, out var log) ? log(line) : Task.CompletedTask;

        public void Remove(string runId) => _runs.TryRemove(runId, out _);
    }
}
