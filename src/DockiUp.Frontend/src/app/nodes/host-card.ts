import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HostStatPointDto, HostStatsDto, NodesService } from '../api';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { formatBytes, num } from '../shared/stores/container-stats.store';
import { formatRate, gpuTotals, hostKey, memPct, pct, timeline, tone } from '../shared/stores/host-stats.store';

export type HostState = 'online' | 'offline' | 'pending';

/** One host on the Nodes page: live usage, disks, GPUs (collapsed to their totals), the last hour, and its actions. */
@Component({
  selector: 'app-host-card',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './host-card.scss',
  template: `
    <article class="x-card host" [class.pending]="state() === 'pending'">
      <header class="head">
        <span class="x-shape tonal"><mat-icon>dns</mat-icon></span>
        <span class="title">
          @if (state() === 'pending') { <span class="name">{{ name() }}</span> }
          @else { <a class="name" [routerLink]="link()">{{ name() }}</a> }
          <span class="sub">{{ subtitle() }}</span>
        </span>
        <span class="state-badge" [attr.data-state]="state()">{{ state() }}</span>
      </header>

      @if (stats(); as s) {
        <div class="usage">
          <div class="ring" [class]="'ring ' + cpuTone()" [style.--p]="cpu()" role="img" [attr.aria-label]="'CPU ' + cpu() + ' percent'">
            <span><b>{{ cpu() }}%</b><small>CPU</small></span>
          </div>
          <div class="mem">
            <div class="row"><span class="label">Memory</span><span class="mono">{{ memText() }}</span></div>
            <div class="bar" aria-hidden="true">
              <span [class]="memTone()" [style.width.%]="memUsedPct()"></span><span class="cache" [style.width.%]="memCachePct()"></span>
            </div>
            <div class="legend">
              <span><i [class]="memTone()"></i>Used {{ bytes(s.memoryUsed) }}</span>
              <span><i class="cache"></i>Cache {{ bytes(s.memoryCache) }}</span>
              @if (num(s.swapTotal) > 0) { <span>Swap {{ bytes(s.swapUsed) }} of {{ bytes(s.swapTotal) }}</span> }
            </div>
          </div>
        </div>

        <div class="tiles">
          <div><span class="k">Load (1m)</span><b>{{ num(s.load1).toFixed(2) }}</b><span class="k">{{ num(s.cores) }} cores</span></div>
          <div><span class="k">Network</span><b>↓ {{ rate(s.networkRxRate) }}</b><span class="k">↑ {{ rate(s.networkTxRate) }}</span></div>
          <div><span class="k">Containers</span><b>{{ containers().running }} / {{ containers().total }}</b><span class="k">{{ containers().projects }} projects</span></div>
        </div>

        <div class="disks">
          <span class="label">Disks</span>
          @for (d of disks(); track d.mount) {
            <div class="disk">
              <span class="mono mount" [title]="d.device ?? ''">{{ d.mount }}</span>
              <span class="bar thin" aria-hidden="true"><span [class]="d.tone" [style.width.%]="d.pct"></span></span>
              <span class="mono" [class.hot-text]="d.tone === 'hot'">{{ d.text }}</span>
            </div>
          }
          @if (!s.hostView) { <span class="hint">Mount <code>/:/host:ro</code> to see every disk and the network.</span> }
        </div>

        @if (gpus().count > 0) {
          <div class="gpus">
            <button type="button" class="gpu-row" (click)="gpuOpen.set(!gpuOpen())" [attr.aria-expanded]="gpuOpen()">
              <span class="x-shape sm gpu-shape"><mat-icon>memory</mat-icon></span>
              <span class="gpu-title">
                <span>{{ gpus().count }} {{ gpus().count === 1 ? 'GPU' : 'GPUs' }}</span>
                <small>{{ gpus().facts }}</small>
              </span>
              <span class="gpu-bars">
                @if (gpus().load != null) {
                  <span class="mini"><span class="k">Load</span><span class="bar thin gpu" aria-hidden="true"><span [style.width.%]="gpus().load"></span></span><span class="mono">{{ gpus().load }}%</span></span>
                }
                @if (gpus().vramTotal > 0) {
                  <span class="mini"><span class="k">VRAM</span><span class="bar thin gpu" aria-hidden="true"><span [style.width.%]="gpus().vramPct"></span></span><span class="mono">{{ gpus().vramText }}</span></span>
                }
              </span>
              <mat-icon class="chevron" [class.open]="gpuOpen()">expand_more</mat-icon>
            </button>
            @if (gpuOpen()) {
              @for (g of s.gpus; track g.index) {
                <div class="gpu-card">
                  <div class="row"><b>{{ g.name }}</b><span class="k">{{ g.vendor }}{{ g.driver ? ' · ' + g.driver : '' }}</span></div>
                  @if (g.utilization != null || g.memoryTotal != null) {
                    <div class="pair">
                      @if (g.utilization != null) {
                        <span class="mini"><span class="k">Load</span><span class="bar thin gpu" aria-hidden="true"><span [style.width.%]="num(g.utilization)"></span></span><span class="mono">{{ num(g.utilization) }}%</span></span>
                      }
                      @if (g.memoryTotal != null) {
                        <span class="mini"><span class="k">VRAM</span><span class="bar thin gpu" aria-hidden="true"><span [style.width.%]="pctOf(g.memoryUsed, g.memoryTotal)"></span></span><span class="mono">{{ bytes(g.memoryUsed) }} / {{ bytes(g.memoryTotal) }}</span></span>
                      }
                    </div>
                  } @else if (g.vendor === 'NVIDIA') {
                    <span class="hint">Give this container GPU access (<code>gpus: all</code>) for live numbers.</span>
                  }
                  <span class="k facts">
                    @if (g.temperature != null) { <span>{{ num(g.temperature) }} °C</span> }
                    @if (g.powerDraw != null) { <span>{{ num(g.powerDraw).toFixed(0) }}{{ g.powerLimit != null ? ' / ' + num(g.powerLimit).toFixed(0) : '' }} W</span> }
                    @if (g.fanPercent != null) { <span>Fan {{ num(g.fanPercent) }}%</span> }
                  </span>
                </div>
              }
            }
          </div>
        }

        <figure class="spark">
          <figcaption>
            <span class="label">Last hour</span>
            <span class="legend">
              <span><i class="line cpu"></i>CPU</span><span><i class="line mem"></i>Memory</span>
              @if (gpus().count > 0) { <span><i class="line gpu"></i>GPU</span> }
            </span>
          </figcaption>
          <svg viewBox="0 0 400 100" preserveAspectRatio="none" role="img" [attr.aria-label]="'CPU and memory of ' + name() + ' over the last hour'">
            <path class="area" [attr.d]="series().cpuArea" />
            <path class="cpu" [attr.d]="series().cpu" />
            <path class="mem" [attr.d]="series().mem" />
            <path class="gpu" [attr.d]="series().gpu" />
          </svg>
        </figure>
      } @else {
        <p class="empty">{{ emptyText() }}</p>
      }

      <footer>
        @if (nodeId() && state() === 'online') {
          <button matButton="tonal" (click)="ping()" [disabled]="pinging()">{{ pinging() ? 'Pinging…' : 'Ping' }}</button>
          <span class="mono k">{{ pingResult() }}</span>
        }
        <span class="spacer"></span>
        @if (nodeId()) {
          <button mat-icon-button class="remove" (click)="remove.emit()" [disabled]="removing()" matTooltip="Remove node" [attr.aria-label]="'Remove node ' + name()">
            <mat-icon>delete</mat-icon>
          </button>
        }
        @if (state() !== 'pending') {
          <a mat-icon-button class="open" [routerLink]="link()" matTooltip="Open" [attr.aria-label]="'Open ' + name()"><mat-icon>chevron_right</mat-icon></a>
        }
      </footer>
    </article>
  `,
})
export class HostCard {
  readonly nodeId = input<string | null>(null);
  readonly name = input.required<string>();
  readonly state = input<HostState>('online');
  readonly lastSeenAt = input<string | null>(null);
  readonly stats = input<HostStatsDto | null>(null);
  readonly lastHour = input<HostStatPointDto[]>([]);
  readonly containers = input<{ running: number; total: number; projects: number }>({ running: 0, total: 0, projects: 0 });
  readonly removing = input(false);
  readonly remove = output<void>();

  private readonly nodes = inject(NodesService);
  private readonly datePipe = new LocalDatePipe();

  protected readonly num = num;
  protected readonly bytes = formatBytes;
  protected readonly rate = formatRate;
  protected readonly pctOf = pct;
  protected readonly gpuOpen = signal(false);
  protected readonly pinging = signal(false);
  protected readonly pingResult = signal('');

  protected readonly link = computed(() => ['/nodes', hostKey(this.nodeId())]);
  protected readonly subtitle = computed(() => {
    const s = this.stats();
    const role = this.nodeId() ? 'Node' : 'This host';
    if (this.state() === 'pending') return `${role} · waiting for it to connect`;
    if (!s) return role;
    return [role, s.os, s.uptimeSeconds != null ? `up ${uptime(num(s.uptimeSeconds))}` : null].filter(Boolean).join(' · ');
  });
  protected readonly emptyText = computed(() => {
    switch (this.state()) {
      case 'pending': return 'Run the node\'s compose on that host; its stats show up as soon as it connects.';
      case 'offline': return this.lastSeenAt() ? `Offline. Last seen ${this.datePipe.transform(this.lastSeenAt())}.` : 'Offline.';
      default: return 'No stats yet. If this stays, update this host to the latest DockiUp.';
    }
  });

  protected readonly cpu = computed(() => Math.round(num(this.stats()?.cpuPercent)));
  protected readonly cpuTone = computed(() => tone(this.cpu()));
  protected readonly memUsedPct = computed(() => pct(this.stats()?.memoryUsed, this.stats()?.memoryTotal));
  protected readonly memCachePct = computed(() => pct(this.stats()?.memoryCache, this.stats()?.memoryTotal));
  protected readonly memTone = computed(() => tone(this.memUsedPct()));
  protected readonly memText = computed(() => {
    const s = this.stats();
    // Cache is given back on demand, so "used" is what programs hold; the bar shows the cache beside it.
    return s ? `${formatBytes(s.memoryUsed)} / ${formatBytes(s.memoryTotal)}` : '';
  });
  protected readonly disks = computed(() => (this.stats()?.disks ?? []).map((d) => {
    const p = pct(d.used, d.total);
    return { mount: d.mount, device: d.device, pct: p, tone: tone(p), text: `${formatBytes(d.used)} / ${formatBytes(d.total)}` };
  }));
  protected readonly gpus = computed(() => {
    const list = this.stats()?.gpus ?? [];
    const t = gpuTotals(list);
    return {
      count: list.length,
      load: t.load,
      vramTotal: t.vramTotal,
      vramPct: pct(t.vramUsed, t.vramTotal),
      vramText: `${formatBytes(t.vramUsed)} / ${formatBytes(t.vramTotal)}`,
      facts: [t.power != null ? `${t.power} W` : null, t.hottest != null ? `max ${t.hottest} °C` : null].filter(Boolean).join(' · '),
    };
  });
  protected readonly series = computed(() => {
    const points = this.lastHour();
    const to = Date.now();
    const from = to - 3_600_000;
    const line = (value: (p: HostStatPointDto) => number | null, area = false) =>
      timeline(points.map((p) => ({ at: p.timestamp, value: value(p) })), from, to, 100, 400, 100, area);
    return {
      cpu: line((p) => num(p.cpuPercent)),
      cpuArea: line((p) => num(p.cpuPercent), true),
      mem: line((p) => memPct(p)),
      gpu: line((p) => (p.gpuUtilization == null ? null : num(p.gpuUtilization))),
    };
  });

  protected ping(): void {
    const id = this.nodeId();
    if (!id) return;
    this.pinging.set(true);
    this.nodes.apiNodesIdPingPost(id).subscribe({
      // roundTripMs is typed as an opaque wrapper by the generator (.NET emits integer|string).
      next: (r) => this.pingResult.set(`answered in ${num(r.roundTripMs)} ms`),
      error: () => this.pingResult.set('unreachable'),
    }).add(() => this.pinging.set(false));
  }
}

/** 3d 4h, 5h 12m, 14m. */
export function uptime(seconds: number): string {
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return d > 0 ? `${d} ${d === 1 ? 'day' : 'days'}` : h > 0 ? `${h}h ${m}m` : `${m}m`;
}
