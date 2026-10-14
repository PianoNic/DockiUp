import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom, interval, map } from 'rxjs';
import { GpuProcessDto, HostStatPointDto, HostsService, NodeDto, NodesService, SpeedTestDto } from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { NotificationService } from '../shared/services/notification.service';
import { ContainerStatsStore, formatBytes, num } from '../shared/stores/container-stats.store';
import { HostStatsStore, formatRate, gpuTotals, memPct, pct, sparkline, timeline, tone } from '../shared/stores/host-stats.store';
import { uptime } from './host-card';

interface Chart { title: string; note: string; a: string; area: string; b: string; legend: [string, string] | null; gpu: boolean }

const RANGES = [{ hours: 1, label: '1 h' }, { hours: 24, label: '24 h' }, { hours: 168, label: '7 d' }];

/** One host in full: live numbers, history, internet connection, GPUs with the containers using them, memory. */
@Component({
  selector: 'app-node-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatTooltipModule, LocalDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './node-detail.scss',
  templateUrl: './node-detail.html',
})
export class NodeDetail {
  /** Route parameter: a node id, or 'local' for this host. */
  private readonly key = toSignal(inject(ActivatedRoute).paramMap.pipe(map((p) => p.get('key') ?? 'local')), { initialValue: 'local' });

  private readonly hostsApi = inject(HostsService);
  private readonly nodesApi = inject(NodesService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly hostStats = inject(HostStatsStore);
  private readonly containerStats = inject(ContainerStatsStore);

  protected readonly ranges = RANGES;
  protected readonly num = num;
  protected readonly bytes = formatBytes;
  protected readonly rate = formatRate;
  protected readonly pctOf = pct;

  protected readonly nodeId = computed(() => (this.key() === 'local' ? null : this.key()));
  protected readonly node = signal<NodeDto | null>(null);
  protected readonly hours = signal(24);
  protected readonly history = signal<HostStatPointDto[]>([]);
  protected readonly speedTests = signal<SpeedTestDto[]>([]);
  protected readonly gpuProcesses = signal<GpuProcessDto[] | null>(null);
  protected readonly testing = signal(false);
  protected readonly pinging = signal(false);
  protected readonly pingResult = signal('');

  protected readonly stats = computed(() => this.hostStats.stats(this.nodeId()));
  protected readonly name = computed(() => this.node()?.name ?? this.stats()?.hostname ?? (this.nodeId() ? 'Node' : 'This host'));
  protected readonly state = computed(() => {
    const n = this.node();
    return !this.nodeId() ? 'online' : !n ? 'offline' : n.online ? 'online' : n.pending ? 'pending' : 'offline';
  });
  protected readonly subtitle = computed(() => {
    const s = this.stats();
    const gpus = s?.gpus?.length ?? 0;
    return [
      this.nodeId() ? 'Node' : 'This host',
      s?.os,
      s ? `${num(s.cores)} cores` : null,
      s ? `${formatBytes(s.memoryTotal)} RAM` : null,
      gpus ? `${gpus} ${gpus === 1 ? 'GPU' : 'GPUs'}` : null,
      s ? `up ${uptime(num(s.uptimeSeconds))}` : null,
    ].filter(Boolean).join(' · ');
  });

  protected readonly gpu = computed(() => {
    const t = gpuTotals(this.stats()?.gpus ?? []);
    return { ...t, vramPct: pct(t.vramUsed, t.vramTotal) };
  });
  protected readonly lastTest = computed(() => [...this.speedTests()].reverse().find((t) => !t.error) ?? null);

  protected readonly kpis = computed(() => {
    const s = this.stats();
    if (!s) return [];
    const disks = s.disks ?? [];
    const diskUsed = disks.reduce((a, d) => a + num(d.used), 0);
    const diskTotal = disks.reduce((a, d) => a + num(d.total), 0);
    const t = this.lastTest();
    const g = this.gpu();
    return [
      { label: 'CPU', value: `${Math.round(num(s.cpuPercent))}%`, sub: `load ${[s.load1, s.load5, s.load15].map((l) => num(l).toFixed(2)).join(' / ')}` },
      { label: 'Memory', value: formatBytes(s.memoryUsed), sub: `of ${formatBytes(s.memoryTotal)} · ${formatBytes(s.memoryCache)} cache` },
      ...(s.gpus?.length ? [{ label: 'GPU', value: g.load != null ? `${g.load}%` : '—', sub: g.vramTotal ? `VRAM ${formatBytes(g.vramUsed)} / ${formatBytes(g.vramTotal)}` : `${s.gpus.length} found` }] : []),
      { label: 'Disk', value: formatBytes(diskUsed), sub: `of ${formatBytes(diskTotal)} on ${disks.length} ${disks.length === 1 ? 'mount' : 'mounts'}` },
      { label: 'Traffic now', value: `↓ ${formatRate(s.networkRxRate)}`, sub: `↑ ${formatRate(s.networkTxRate)}${t ? ` · line is ${num(t.downloadMbps).toFixed(0)} / ${num(t.uploadMbps).toFixed(0)} Mbit/s` : ''}` },
    ];
  });

  protected readonly charts = computed<Chart[]>(() => {
    const h = this.history();
    const avgPeak = (values: number[], unit: (v: number) => string) =>
      values.length ? `avg ${unit(values.reduce((a, b) => a + b, 0) / values.length)} · peak ${unit(Math.max(...values))}` : 'no data yet';
    const percent = (v: number) => `${Math.round(v)}%`;
    const cpu = h.map((p) => num(p.cpuPercent));
    const mem = h.map((p) => memPct(p));
    const rx = h.map((p) => (p.networkRxRate == null ? null : num(p.networkRxRate)));
    const tx = h.map((p) => (p.networkTxRate == null ? null : num(p.networkTxRate)));
    const rd = h.map((p) => (p.diskReadRate == null ? null : num(p.diskReadRate)));
    const wr = h.map((p) => (p.diskWriteRate == null ? null : num(p.diskWriteRate)));
    const peak = (...series: (number | null)[][]) => Math.max(1, ...series.flat().map((v) => v ?? 0)) * 1.1;
    const netMax = peak(rx, tx);
    const ioMax = peak(rd, wr);
    const known = (v: (number | null)[]) => v.filter((x): x is number => x != null);
    // Placed by time over the chosen range, so a young host's short history doesn't look like a full day.
    const to = Date.now();
    const from = to - this.hours() * 3_600_000;
    const line = (values: (number | null)[], max: number, area = false) =>
      timeline(h.map((p, i) => ({ at: p.timestamp, value: values[i] })), from, to, max, 600, 160, area);
    const chart = (title: string, note: string, a: (number | null)[], max: number, b: (number | null)[] | null = null, legend: [string, string] | null = null, gpu = false): Chart =>
      ({ title, note, a: line(a, max), area: line(a, max, true), b: b ? line(b, max) : '', legend, gpu });
    const charts: Chart[] = [
      chart('CPU', avgPeak(cpu, percent), cpu, 100),
      chart('Memory', avgPeak(mem, percent), mem, 100),
      chart('Network', `peak ↓ ${formatRate(Math.max(0, ...known(rx)))} · ↑ ${formatRate(Math.max(0, ...known(tx)))}`, rx, netMax, tx, ['Download', 'Upload']),
      chart('Disk I/O', `peak read ${formatRate(Math.max(0, ...known(rd)))} · write ${formatRate(Math.max(0, ...known(wr)))}`, rd, ioMax, wr, ['Read', 'Write']),
    ];
    if (h.some((p) => p.gpuUtilization != null)) {
      const g = h.map((p) => (p.gpuUtilization == null ? null : num(p.gpuUtilization)));
      charts.push(chart('GPU load', avgPeak(known(g), percent), g, 100, null, null, true));
    }
    if (h.some((p) => p.gpuMemoryTotal != null)) {
      const v = h.map((p) => (p.gpuMemoryTotal == null ? null : pct(p.gpuMemoryUsed, p.gpuMemoryTotal)));
      charts.push(chart('VRAM', avgPeak(known(v), percent), v, 100, null, null, true));
    }
    return charts;
  });

  protected readonly speedChart = computed(() => {
    const ok = this.speedTests().filter((t) => !t.error);
    const max = Math.max(1, ...ok.flatMap((t) => [num(t.downloadMbps), num(t.uploadMbps)])) * 1.1;
    return {
      down: sparkline(ok.map((t) => num(t.downloadMbps)), max, 600, 140),
      up: sparkline(ok.map((t) => num(t.uploadMbps)), max, 600, 140),
      max: Math.round(max),
      low: ok.length ? Math.min(...ok.map((t) => num(t.downloadMbps))) : null,
      count: ok.length,
    };
  });
  protected readonly inUse = computed(() => {
    const s = this.stats();
    const t = this.lastTest();
    if (!s || !t || s.networkRxRate == null) return null;
    const down = (num(s.networkRxRate) * 8) / 1_000_000;
    const up = (num(s.networkTxRate) * 8) / 1_000_000;
    return { down, up, pct: Math.max(pct(down, t.downloadMbps), pct(up, t.uploadMbps)) };
  });

  protected readonly memParts = computed(() => {
    const s = this.stats();
    if (!s) return [];
    const total = num(s.memoryTotal);
    const used = num(s.memoryUsed);
    const cache = num(s.memoryCache);
    return [
      { label: 'Used by programs', bytes: used, cls: tone(pct(used, total)) },
      { label: 'Cache', bytes: cache, cls: 'cache' },
      { label: 'Free', bytes: Math.max(0, total - used - cache), cls: 'free' },
    ].map((p) => ({ ...p, pct: pct(p.bytes, total) }));
  });
  protected readonly topContainers = computed(() => {
    const id = this.nodeId();
    const list = [...this.containerStats.byContainer().values()].filter((c) => (c.nodeId ?? null) === id)
      .sort((a, b) => num(b.memoryUsage) - num(a.memoryUsage)).slice(0, 6);
    const max = Math.max(1, ...list.map((c) => num(c.memoryUsage)));
    return list.map((c) => ({ name: c.containerName, mem: formatBytes(c.memoryUsage), cpu: `${num(c.cpuPercent).toFixed(0)}%`, pct: pct(c.memoryUsage, max) }));
  });

  protected readonly tone = tone;
  /** 10000 → "10 Gbit/s", 100 → "100 Mbit/s". */
  protected readonly linkSpeed = computed(() => {
    const m = num(this.stats()?.linkSpeedMbps);
    return m >= 1000 ? `${+(m / 1000).toFixed(1)} Gbit/s` : `${m} Mbit/s`;
  });

  constructor() {
    this.hostStats.start();
    void this.containerStats.ensureLoaded();
    effect(() => {
      const id = this.nodeId();
      void this.loadHistory(id, this.hours());
    });
    effect(() => {
      const id = this.nodeId();
      void this.loadNode(id);
      void this.loadSpeedTests(id);
      void this.loadGpuProcesses(id);
    });
    // Keep the history and GPU list moving without hammering the host.
    interval(60_000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      void this.loadHistory(this.nodeId(), this.hours());
      void this.loadGpuProcesses(this.nodeId());
      void this.loadNode(this.nodeId());
    });
  }

  private async loadNode(id: string | null): Promise<void> {
    if (!id) return;
    try {
      this.node.set((await firstValueFrom(this.nodesApi.apiNodesGet())).find((n) => n.id === id) ?? null);
    } catch { /* keep the last one */ }
  }

  private async loadHistory(id: string | null, hours: number): Promise<void> {
    try {
      this.history.set(await firstValueFrom(this.hostsApi.getHostHistory(id ?? undefined, hours)));
    } catch { /* keep the last one */ }
  }

  private async loadSpeedTests(id: string | null): Promise<void> {
    try {
      this.speedTests.set(await firstValueFrom(this.hostsApi.listSpeedTests(id ?? undefined, 7)));
    } catch { /* keep the last one */ }
  }

  private async loadGpuProcesses(id: string | null): Promise<void> {
    if (!(this.stats()?.gpus?.length)) { this.gpuProcesses.set(null); return; }
    try {
      this.gpuProcesses.set(await firstValueFrom(this.hostsApi.getGpuProcesses(id ?? undefined)));
    } catch { this.gpuProcesses.set(null); }
  }

  protected async runSpeedTest(): Promise<void> {
    this.testing.set(true);
    try {
      const t = await firstValueFrom(this.hostsApi.runSpeedTest(this.nodeId() ?? undefined));
      if (t.error) this.toast.showError('Speed test failed', t.error);
      else this.toast.success(`↓ ${num(t.downloadMbps).toFixed(0)} Mbit/s · ↑ ${num(t.uploadMbps).toFixed(0)} Mbit/s · ${num(t.pingMs).toFixed(0)} ms`);
      await this.loadSpeedTests(this.nodeId());
    } catch (err) {
      this.toast.showError('Speed test failed', err);
    } finally {
      this.testing.set(false);
    }
  }

  protected ping(): void {
    const id = this.nodeId();
    if (!id) return;
    this.pinging.set(true);
    this.nodesApi.apiNodesIdPingPost(id).subscribe({
      next: (r) => this.pingResult.set(`answered in ${num(r.roundTripMs)} ms`),
      error: () => this.pingResult.set('unreachable'),
    }).add(() => this.pinging.set(false));
  }

  protected async remove(): Promise<void> {
    const id = this.nodeId();
    if (!id) return;
    const ok = await this.confirm.ask({
      title: `Remove ${this.name()}?`,
      message: 'DockiUp forgets this node and its token stops working. Its containers keep running on that host; stop the node container there afterwards.',
      confirmText: 'Remove node',
      destructive: true,
    });
    if (!ok) return;
    try {
      await firstValueFrom(this.nodesApi.apiNodesIdDelete(id));
      await this.router.navigate(['/nodes']);
    } catch (err) {
      this.toast.showError('Could not remove the node', err);
    }
  }
}
