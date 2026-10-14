using System.Collections.Concurrent;
using DockiUp.Application.Dtos;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Monitoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DockiUp.API.Controllers
{
    /// <summary>The machines themselves: this host (nodeId omitted) and every node. Live readings, history, GPU use per
    /// container and the internet speed test. Listing, pinging and removing nodes stays in /api/Nodes.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HostsController(IDockiUpDbContext db, HostStatsStore store, IDockerServiceResolver resolver, IServiceProvider services) : ControllerBase
    {
        /// <summary>Every host with its live reading (null while offline or not read yet), its last hour and its latest speed test.</summary>
        [HttpGet(Name = "ListHostSummaries")]
        [ProducesResponseType(typeof(HostSummaryDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<HostSummaryDto[]>> ListHostSummaries(CancellationToken cancellationToken)
        {
            Guid?[] hosts = [null, .. await db.Nodes.Select(n => (Guid?)n.Id).ToListAsync(cancellationToken)];
            var tests = await LatestSpeedTestsAsync(cancellationToken);
            var result = new List<HostSummaryDto>();
            foreach (var nodeId in hosts)
                result.Add(new HostSummaryDto(nodeId, store.Get(nodeId),
                    await HostStatsHistory.QueryAsync(db, nodeId, 1, DateTime.UtcNow, cancellationToken),
                    tests.GetValueOrDefault(nodeId?.ToString() ?? "local")));
            return Ok(result.ToArray());
        }

        /// <summary>A host's history: one point per minute up to six hours, coarser beyond (about 360 points).</summary>
        [HttpGet("History", Name = "GetHostHistory")]
        [ProducesResponseType(typeof(HostStatPointDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<HostStatPointDto[]>> GetHostHistory([FromQuery] Guid? nodeId, [FromQuery] int hours = 24, CancellationToken cancellationToken = default)
            => Ok(await HostStatsHistory.QueryAsync(db, nodeId, hours, DateTime.UtcNow, cancellationToken));

        /// <summary>Processes holding GPU memory on the host, with their container and project.</summary>
        [HttpGet("GpuProcesses", Name = "GetGpuProcesses")]
        [ProducesResponseType(typeof(GpuProcessDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<GpuProcessDto[]>> GetGpuProcesses([FromQuery] Guid? nodeId, CancellationToken cancellationToken)
            => Ok(await resolver.Resolve(nodeId).GetGpuProcessesAsync(cancellationToken));

        [HttpGet("SpeedTests", Name = "ListSpeedTests")]
        [ProducesResponseType(typeof(SpeedTestDto[]), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpeedTestDto[]>> ListSpeedTests([FromQuery] Guid? nodeId, [FromQuery] int days = 7, CancellationToken cancellationToken = default)
        {
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 30));
            return Ok(await db.SpeedTestResults.AsNoTracking().Where(r => r.NodeId == nodeId && r.At >= since).OrderBy(r => r.At)
                .Select(r => HostStatsHistory.ToDto(r)).ToArrayAsync(cancellationToken));
        }

        /// <summary>Runs the speed test on the host now (about 15 seconds) and stores the result.</summary>
        [HttpPost("SpeedTest", Name = "RunSpeedTest")]
        [ProducesResponseType(typeof(SpeedTestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SpeedTestDto>> RunSpeedTest([FromQuery] Guid? nodeId, CancellationToken cancellationToken)
            => Ok(await RunSpeedTestAsync(services, nodeId, cancellationToken));

        // One test per host at a time: two at once would split the line and both be wrong.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Running = new();

        public static async Task<SpeedTestDto> RunSpeedTestAsync(IServiceProvider services, Guid? nodeId, CancellationToken cancellationToken)
        {
            var gate = Running.GetOrAdd(nodeId?.ToString() ?? "local", _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, cancellationToken)) throw new ArgumentException("A speed test is already running on this host.");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                var test = await services.GetRequiredService<IDockerServiceResolver>().Resolve(nodeId).RunSpeedTestAsync(timeout.Token);
                return await HostStatsHistory.SaveSpeedTestAsync(services.GetRequiredService<IDockiUpDbContext>(), nodeId, test, cancellationToken);
            }
            finally { gate.Release(); }
        }

        private async Task<Dictionary<string, SpeedTestDto>> LatestSpeedTestsAsync(CancellationToken cancellationToken)
            => (await db.SpeedTestResults.AsNoTracking().OrderByDescending(r => r.At).Take(500).ToListAsync(cancellationToken))
                .GroupBy(r => r.NodeId?.ToString() ?? "local")
                .ToDictionary(g => g.Key, g => HostStatsHistory.ToDto(g.First()));
    }
}
