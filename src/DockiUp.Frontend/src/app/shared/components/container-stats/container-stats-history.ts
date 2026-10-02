import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom } from 'rxjs';
import { ContainerStatsPointDto, StatsService } from '../../../api';
import { ChartPoint, LineChart } from '../line-chart/line-chart';
import { formatBytes, num } from '../../stores/container-stats.store';

/** CPU and memory history (1-minute points) of one container over the last 1/6/24 hours. Two charts, one measure each. */
@Component({
  selector: 'app-container-stats-history',
  imports: [LineChart, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="range">
      <span>History</span>
      <div class="x-group" role="radiogroup" aria-label="History range">
        @for (h of ranges; track h) {
          <button type="button" role="radio" [matButton]="hours() === h ? 'filled' : 'tonal'" [class.x-on]="hours() === h"
            [attr.aria-checked]="hours() === h" (click)="hours.set(h)">{{ h }}h</button>
        }
      </div>
    </div>
    <app-line-chart label="CPU" [points]="cpu()" [floor]="1" [format]="percent" [from]="from()" [to]="to()" />
    <app-line-chart label="Memory" [points]="memory()" [format]="bytes" [from]="from()" [to]="to()" />
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 10px; padding-top: 6px; }
    .range { display: flex; align-items: center; justify-content: space-between; font-size: 12px; color: var(--mat-sys-on-surface-variant); }
    .x-group button { height: 32px; padding: 0 14px; font-size: 13px; }
  `,
})
export class ContainerStatsHistory {
  private readonly api = inject(StatsService);
  readonly containerName = input.required<string>();
  readonly nodeId = input<string | null | undefined>(null);

  protected readonly ranges = [1, 6, 24];
  protected readonly hours = signal(1);
  protected readonly from = signal(Date.now() - 3600_000);
  protected readonly to = signal(Date.now());
  protected readonly cpu = signal<ChartPoint[]>([]);
  protected readonly memory = signal<ChartPoint[]>([]);

  protected readonly percent = (v: number) => `${v.toFixed(1)}%`;
  protected readonly bytes = (v: number) => formatBytes(v);

  constructor() {
    effect(() => void this.load(this.containerName(), this.nodeId() ?? undefined, this.hours()));
    // New points are persisted once a minute.
    const timer = setInterval(() => void this.load(this.containerName(), this.nodeId() ?? undefined, this.hours()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  private async load(name: string, nodeId: string | undefined, hours: number): Promise<void> {
    let points: ContainerStatsPointDto[] = [];
    try {
      points = await firstValueFrom(this.api.getContainerStatsHistory(name, nodeId, hours as never));
    } catch {
      points = [];
    }
    const now = Date.now();
    this.from.set(now - hours * 3600_000);
    this.to.set(now);
    const at = (p: ContainerStatsPointDto) => new Date(p.timestamp).getTime();
    this.cpu.set(points.map((p) => ({ t: at(p), v: num(p.cpuPercent) })));
    this.memory.set(points.map((p) => ({ t: at(p), v: num(p.memoryUsage) })));
  }
}
