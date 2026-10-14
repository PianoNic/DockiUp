import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { NodeDto, NodesService } from '../api';
import { ProjectStore } from '../shared/stores/project.store';
import { formatBytes, num } from '../shared/stores/container-stats.store';
import { HostStatsStore, containersPerHost, formatRate, gpuTotals, hostKey, pct, tone } from '../shared/stores/host-stats.store';

/** How many hosts get a card; the rest become one chip each. */
const CARDS = 4;

interface Meter { label: string; pct: number; value: string; tone: string; gpu: boolean }

/** The dashboard's small look at every host: the busiest few as compact cards, the others as chips, so the strip stays
 *  one row however many hosts there are. The Nodes page has the full view. */
@Component({
  selector: 'app-host-strip',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './host-strip.scss',
  template: `
    @if (cards().length) {
      <section class="strip" aria-label="Hosts">
        <div class="cards">
          @for (h of cards(); track h.key) {
            <a class="card" [class.hot]="h.peak >= 85" [routerLink]="['/nodes', h.key]">
              <span class="head">
                <i class="dot" [class]="'dot ' + h.dot" aria-hidden="true"></i>
                <b>{{ h.name }}</b>
                <span class="k grow">{{ h.containers }}</span>
                <span class="mono k">↓{{ h.rx }} ↑{{ h.tx }}</span>
              </span>
              @for (m of h.meters; track m.label) {
                <span class="meter">
                  <span class="k">{{ m.label }}</span>
                  <span class="bar thin" [class.gpu]="m.gpu" aria-hidden="true"><span [class]="m.gpu ? '' : m.tone" [style.width.%]="m.pct"></span></span>
                  <span class="mono" [class.hot-text]="m.tone === 'hot'">{{ m.value }}</span>
                </span>
              }
            </a>
          }
        </div>
        @if (chips().length) {
          <div class="chips">
            @for (c of chips(); track c.key) {
              <a class="chip" [routerLink]="['/nodes', c.key]"><i [class]="'dot ' + c.dot" aria-hidden="true"></i><b>{{ c.name }}</b><span class="mono k">{{ c.top }}</span></a>
            }
            <a class="all" routerLink="/nodes">All {{ total() }} hosts →</a>
          </div>
        }
      </section>
    }
  `,
})
export class HostStrip {
  private readonly hostStats = inject(HostStatsStore);
  private readonly projectStore = inject(ProjectStore);
  private readonly nodes = signal<NodeDto[]>([]);

  constructor() {
    this.hostStats.start();
    firstValueFrom(inject(NodesService).apiNodesGet()).then((n) => this.nodes.set(n)).catch(() => {});
  }

  private readonly hosts = computed(() => {
    const containers = containersPerHost(this.projectStore.projectDtos());
    const list = [
      { nodeId: null as string | null, name: this.hostStats.stats(null)?.hostname || 'This host', online: true },
      ...this.nodes().filter((n) => !n.pending).map((n) => ({ nodeId: n.id as string | null, name: n.name, online: n.online })),
    ];
    return list.map((h) => {
      const s = this.hostStats.stats(h.nodeId);
      const c = containers.get(hostKey(h.nodeId));
      const meters: Meter[] = [];
      if (s) {
        const meter = (label: string, p: number, value: string, gpu = false): Meter => ({ label, pct: p, value, tone: tone(p), gpu });
        meters.push(meter('CPU', Math.round(num(s.cpuPercent)), `${Math.round(num(s.cpuPercent))}%`));
        meters.push(meter('RAM', pct(s.memoryUsed, s.memoryTotal), `${formatBytes(s.memoryUsed)} / ${formatBytes(s.memoryTotal)}`));
        if (s.gpus?.length) {
          const g = gpuTotals(s.gpus);
          meters.push(meter('GPU', g.load ?? pct(g.vramUsed, g.vramTotal), g.load != null ? `${g.load}%` : `${pct(g.vramUsed, g.vramTotal)}% VRAM`, true));
        } else {
          const fullest = Math.max(0, ...(s.disks ?? []).map((d) => pct(d.used, d.total)));
          meters.push(meter('Disk', fullest, `${fullest}%`));
        }
      }
      const peak = Math.max(0, ...meters.map((m) => m.pct));
      const top = meters.reduce<Meter | null>((a, m) => (!a || m.pct > a.pct ? m : a), null);
      return {
        key: hostKey(h.nodeId),
        name: h.name,
        online: h.online,
        peak,
        dot: !h.online ? 'off' : peak >= 85 ? 'hot' : 'ok',
        containers: c ? `${c.running}/${c.total} up` : '',
        rx: formatRate(s?.networkRxRate).replace('/s', ''),
        tx: formatRate(s?.networkTxRate).replace('/s', ''),
        meters,
        top: !h.online ? 'offline' : top ? `${top.label} ${top.pct}%` : '—',
      };
    });
  });

  // Busiest first: online hosts by their highest meter, offline ones last.
  private readonly sorted = computed(() => [...this.hosts()].sort((a, b) => Number(b.online) - Number(a.online) || b.peak - a.peak));
  protected readonly cards = computed(() => this.sorted().filter((h) => h.online && h.meters.length).slice(0, CARDS));
  protected readonly chips = computed(() => {
    const shown = new Set(this.cards().map((h) => h.key));
    return this.sorted().filter((h) => !shown.has(h.key));
  });
  protected readonly total = computed(() => this.hosts().length);
}
