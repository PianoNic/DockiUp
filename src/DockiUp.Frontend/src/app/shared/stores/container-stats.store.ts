import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ContainerStatsDto, StatsService } from '../../api';

/** Numbers from the generated client may be typed as opaque wrappers (.NET emits integer|string); read them as numbers. */
export function num(value: unknown): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

/** Binary units, one decimal: 1536 -> "1.5 KB". */
export function formatBytes(value: unknown): string {
  let n = num(value);
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i++;
  }
  return i === 0 ? `${n} B` : `${n.toFixed(n >= 100 ? 0 : 1)} ${units[i]}`;
}

/** Live CPU / memory / network per running container, keyed by container id. Seeded over HTTP once,
 *  then replaced by the server's ContainerStats SignalR push (every sampling interval). */
@Injectable({ providedIn: 'root' })
export class ContainerStatsStore {
  private readonly api = inject(StatsService);
  private loaded = false;

  readonly byContainer = signal<ReadonlyMap<string, ContainerStatsDto>>(new Map());

  set(stats: ContainerStatsDto[]): void {
    this.byContainer.set(new Map(stats.map((s) => [s.containerId, s])));
  }

  /** Fills the store before the first push arrives (a sampling round can be up to 15s away). */
  async ensureLoaded(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;
    try {
      this.set(await firstValueFrom(this.api.getContainerStats()));
    } catch {
      this.loaded = false; // try again next time a page asks
    }
  }

  get(containerId: string): ContainerStatsDto | undefined {
    return this.byContainer().get(containerId);
  }
}
