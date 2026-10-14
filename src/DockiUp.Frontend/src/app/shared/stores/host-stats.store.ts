import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GpuStatsDto, HostStatPointDto, HostStatsDto, HostSummaryDto, HostsService, ProjectDto } from '../../api';
import { UpdateMethodType, normalizeContainerState } from '../models/api-enums';
import { formatBytes, num } from './container-stats.store';

/** Key of a host in maps and URLs: its node id, or 'local' for the control plane itself. */
export const hostKey = (nodeId?: string | null): string => nodeId ?? 'local';

/** Bytes per second as "2.1 MB/s"; '—' when the host can't tell. */
export function formatRate(value: unknown): string {
  return value == null ? '—' : `${formatBytes(num(value))}/s`;
}

/** Colour step of a usage bar: blue, amber from 70 %, red from 85 %. */
export function tone(pct: number): 'ok' | 'warm' | 'hot' {
  return pct >= 85 ? 'hot' : pct >= 70 ? 'warm' : 'ok';
}

export function pct(part: unknown, whole: unknown): number {
  const w = num(whole);
  return w > 0 ? Math.min(100, Math.round((num(part) / w) * 100)) : 0;
}

/** All GPUs of a host as one: average load over those reporting one, summed VRAM and power, the hottest. */
export function gpuTotals(gpus: GpuStatsDto[]) {
  const loads = gpus.filter((g) => g.utilization != null).map((g) => num(g.utilization));
  const withVram = gpus.filter((g) => g.memoryTotal != null);
  const withPower = gpus.filter((g) => g.powerDraw != null);
  const temps = gpus.filter((g) => g.temperature != null).map((g) => num(g.temperature));
  return {
    load: loads.length ? Math.round(loads.reduce((a, b) => a + b, 0) / loads.length) : null,
    vramUsed: withVram.reduce((s, g) => s + num(g.memoryUsed), 0),
    vramTotal: withVram.reduce((s, g) => s + num(g.memoryTotal), 0),
    power: withPower.length ? Math.round(withPower.reduce((s, g) => s + num(g.powerDraw), 0)) : null,
    hottest: temps.length ? Math.max(...temps) : null,
  };
}

/** An SVG path (viewBox 0 0 400 100) through the points' values, 0..max mapped bottom to top. '' with fewer than two points. */
export function sparkline(values: (number | null)[], max = 100, width = 400, height = 100): string {
  const known = values.map((v, i) => [i, v] as const).filter((p): p is readonly [number, number] => p[1] != null);
  if (known.length < 2 || values.length < 2) return '';
  const x = (i: number) => ((i / (values.length - 1)) * width).toFixed(1);
  const y = (v: number) => (height - Math.min(1, Math.max(0, v / (max || 1))) * height).toFixed(1);
  return 'M' + known.map(([i, v]) => `${x(i)},${y(v)}`).join(' L');
}

/** The same line closed to the bottom, for a filled area. */
export function sparkArea(values: (number | null)[], max = 100, width = 400, height = 100): string {
  const line = sparkline(values, max, width, height);
  return line ? `${line} L${width},${height} L0,${height} Z` : '';
}

/** A line placed by time over [from, to] (ms), so a short history covers only its part of the range.
 *  Gaps longer than five minutes (the host was off) break the line. */
export function timeline(points: { at: string; value: number | null }[], from: number, to: number, max = 100, width = 400, height = 100, area = false): string {
  const span = Math.max(1, to - from);
  const x = (t: number) => (Math.min(1, Math.max(0, (t - from) / span)) * width).toFixed(1);
  const y = (v: number) => (height - Math.min(1, Math.max(0, v / (max || 1))) * height).toFixed(1);
  const runs: string[][] = [];
  let last = -Infinity;
  for (const p of points) {
    const t = new Date(p.at).getTime();
    if (p.value == null || t < from) { last = -Infinity; continue; }
    if (t - last > 5 * 60_000 || !runs.length) runs.push([]);
    runs[runs.length - 1].push(`${x(t)},${y(p.value)}`);
    last = t;
  }
  return runs.filter((r) => r.length > 1).map((r) => {
    const line = 'M' + r.join(' L');
    if (!area) return line;
    const [x0] = r[0].split(','), [x1] = r[r.length - 1].split(',');
    return `${line} L${x1},${height} L${x0},${height} Z`;
  }).join(' ');
}

export function memPct(p: HostStatPointDto | HostStatsDto): number {
  return pct(num(p.memoryUsed), p.memoryTotal);
}

/** Every host's live reading (pushed over SignalR as HostStats), its last hour and latest speed test.
 *  The summaries are fetched once and then every minute; the live readings replace their stats in between. */
@Injectable({ providedIn: 'root' })
export class HostStatsStore {
  private readonly api = inject(HostsService);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly summaries = signal<HostSummaryDto[]>([]);
  private readonly live = signal<ReadonlyMap<string, HostStatsDto>>(new Map());

  setLive(stats: HostStatsDto[]): void {
    this.live.set(new Map(stats.map((s) => [hostKey(s.nodeId), s])));
  }

  /** Starts the minute refresh (once per app). */
  start(): void {
    if (this.timer) return;
    void this.refresh();
    this.timer = setInterval(() => void this.refresh(), 60_000);
  }

  async refresh(): Promise<void> {
    try {
      const summaries = await firstValueFrom(this.api.listHostSummaries());
      this.summaries.set(summaries);
      // Readings that came with the summaries count as live until the next push.
      this.live.update((m) => {
        const next = new Map(m);
        for (const s of summaries) if (s.stats && !next.has(hostKey(s.nodeId))) next.set(hostKey(s.nodeId), s.stats);
        return next;
      });
    } catch {
      /* offline for a moment: keep what we have */
    }
  }

  stats(nodeId?: string | null): HostStatsDto | null {
    return this.live().get(hostKey(nodeId)) ?? null;
  }

  summary(nodeId?: string | null): HostSummaryDto | null {
    return this.summaries().find((s) => hostKey(s.nodeId) === hostKey(nodeId)) ?? null;
  }
}

/** Running and total containers, and projects, per host key. */
export function containersPerHost(projects: ProjectDto[]): Map<string, { running: number; total: number; projects: number }> {
  const by = new Map<string, { running: number; total: number; projects: number }>();
  for (const p of projects) {
    const key = hostKey(p.nodeId);
    const c = by.get(key) ?? { running: 0, total: 0, projects: 0 };
    c.projects++;
    for (const container of p.containers ?? []) {
      c.total++;
      if (normalizeContainerState(container.state) === UpdateMethodType.Running) c.running++;
    }
    by.set(key, c);
  }
  return by;
}
