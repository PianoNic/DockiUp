import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ContainerStatsStore, formatBytes, num } from '../../stores/container-stats.store';

/** Live CPU and memory of one running container (from the SignalR-fed stats store), as two thin meters. */
@Component({
  selector: 'app-container-stats-meter',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <div class="meter" [matTooltip]="'Network: ' + v.rx + ' in, ' + v.tx + ' out (since start)'">
        <span class="name">CPU</span>
        <span class="bar"><span [style.width.%]="v.cpuBar"></span></span>
        <span class="val">{{ v.cpu }}</span>
        <span class="name">MEM</span>
        <span class="bar"><span [style.width.%]="v.memBar"></span></span>
        <span class="val">{{ v.mem }}</span>
      </div>
    }
  `,
  styles: `
    .meter { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: 2px 8px; font-size: 11.5px; }
    .name { color: var(--mat-sys-on-surface-variant); font-weight: 500; letter-spacing: 0.04em; }
    .val { color: var(--mat-sys-on-surface); font-variant-numeric: tabular-nums; text-align: right; white-space: nowrap; }
    .bar { height: 4px; border-radius: 2px; background: var(--mat-sys-surface-container-highest); overflow: hidden; }
    .bar > span { display: block; height: 100%; border-radius: 2px; background: var(--mat-sys-primary); }
  `,
})
export class ContainerStatsMeter {
  private readonly store = inject(ContainerStatsStore);
  readonly containerId = input.required<string>();

  protected readonly view = computed(() => {
    const s = this.store.byContainer().get(this.containerId());
    if (!s) return null;
    const cpu = num(s.cpuPercent);
    const used = num(s.memoryUsage);
    const limit = num(s.memoryLimit);
    return {
      cpu: `${cpu.toFixed(1)}%`,
      // CPU can exceed 100% (one core = 100%); the bar shows it capped.
      cpuBar: Math.min(100, cpu),
      mem: limit > 0 ? `${formatBytes(used)} / ${formatBytes(limit)}` : formatBytes(used),
      memBar: limit > 0 ? Math.min(100, (used / limit) * 100) : 0,
      rx: formatBytes(s.networkRx),
      tx: formatBytes(s.networkTx),
    };
  });

  constructor() {
    void this.store.ensureLoaded();
  }
}
