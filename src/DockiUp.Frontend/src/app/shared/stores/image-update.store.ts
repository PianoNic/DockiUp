import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ImageUpdateDto, ImageUpdatesService } from '../../api';
import { NotificationService } from '../services/notification.service';

/** Results of the registry image checks (#68), live over SignalR (ImageUpdatesChanged). */
@Injectable({ providedIn: 'root' })
export class ImageUpdateStore {
  private readonly api = inject(ImageUpdatesService);
  private readonly notifications = inject(NotificationService);

  /** Every checked service of every project, including ones that could not be compared. */
  readonly all = signal<ImageUpdateDto[]>([]);
  readonly checking = signal(false);
  private loaded = false;

  /** projectId -> services with a newer image. */
  readonly available = computed(() => {
    const map = new Map<string, ImageUpdateDto[]>();
    for (const u of this.all()) {
      if (u.updateAvailable) map.set(u.projectId, [...(map.get(u.projectId) ?? []), u]);
    }
    return map;
  });

  updatesFor(projectId: string | null | undefined): ImageUpdateDto[] {
    return projectId ? (this.available().get(projectId) ?? []) : [];
  }

  /** Loads once; later changes arrive over SignalR. */
  async ensureLoaded(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;
    await this.load();
  }

  async load(): Promise<void> {
    try {
      this.all.set(await firstValueFrom(this.api.listImageUpdates()));
    } catch {
      // Badges are best-effort; the pages work without them.
    }
  }

  set(updates: ImageUpdateDto[]): void {
    this.all.set(updates ?? []);
  }

  /** Asks the registries now (one project or all) and reports what was found. */
  async checkNow(projectId?: string): Promise<void> {
    this.checking.set(true);
    try {
      const all = await firstValueFrom(this.api.checkImageUpdates(projectId));
      this.all.set(all);
      const found = all.filter((u) => u.updateAvailable && (!projectId || u.projectId === projectId)).length;
      this.notifications.success(found ? `${found} image update${found === 1 ? '' : 's'} available` : 'All images are up to date');
    } catch (err) {
      this.notifications.showError('Image update check failed', err);
    } finally {
      this.checking.set(false);
    }
  }
}

/** Tooltip text naming the affected services. */
export function updatesTooltip(updates: ImageUpdateDto[]): string {
  return 'Newer image for: ' + updates.map((u) => `${u.serviceName} (${u.image})`).join(', ');
}
