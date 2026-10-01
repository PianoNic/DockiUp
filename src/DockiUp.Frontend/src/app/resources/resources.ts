import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import {
  CleanupFrequency,
  CleanupScheduleDto,
  DockerImageDto,
  DockerNetworkDto,
  DockerResourcesDto,
  DockerVolumeDto,
  NodeDto,
  NodesService,
  PruneRequest,
  PruneResultDto,
  ResourceKind,
  ResourcesService,
} from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { NotificationService } from '../shared/services/notification.service';
import { formatBytes, num } from '../shared/stores/container-stats.store';

/** The schedule is stored in UTC; the UI edits it in the browser's local time. Day: 0 = Sunday. */
export function localToUtc(day: number, hour: number): { day: number; hour: number } {
  const d = new Date();
  d.setHours(hour, 0, 0, 0);
  d.setDate(d.getDate() + ((day - d.getDay() + 7) % 7));
  return { day: d.getUTCDay(), hour: d.getUTCHours() };
}

export function utcToLocal(day: number, hour: number): { day: number; hour: number } {
  const d = new Date();
  d.setUTCHours(hour, 0, 0, 0);
  d.setUTCDate(d.getUTCDate() + ((day - d.getUTCDay() + 7) % 7));
  return { day: d.getDay(), hour: d.getHours() };
}

/** Images, volumes and networks of one host (the control plane or a node), with prune and the scheduled cleanup. */
@Component({
  selector: 'app-resources',
  imports: [
    LocalDatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTabsModule,
    MatTooltipModule,
  ],
  templateUrl: './resources.html',
  styleUrls: ['../activity/activity.scss', './resources.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Resources {
  private readonly api = inject(ResourcesService);
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);

  protected readonly bytes = formatBytes;
  protected readonly days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  protected readonly hours = Array.from({ length: 24 }, (_, h) => h);

  protected readonly nodes = signal<NodeDto[]>([]);
  /** '' = the control-plane host. */
  protected readonly host = signal('');
  protected readonly hostName = computed(() => (this.host() ? (this.nodes().find((n) => n.id === this.host())?.name ?? 'node') : 'Local host'));

  protected readonly data = signal<DockerResourcesDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly filter = signal('');
  protected readonly tab = signal(0);

  private matches(...values: (string | null | undefined)[]): boolean {
    const q = this.filter().toLowerCase().trim();
    return !q || values.some((v) => v?.toLowerCase().includes(q));
  }

  protected readonly images = computed(() => (this.data()?.images ?? []).filter((i) => this.matches(this.imageName(i), i.id)));
  protected readonly volumes = computed(() => (this.data()?.volumes ?? []).filter((v) => this.matches(v.name, v.driver)));
  protected readonly networks = computed(() => (this.data()?.networks ?? []).filter((n) => this.matches(n.name, n.driver, n.id)));
  protected readonly unused = computed(() => {
    const d = this.data();
    return {
      images: d?.images.filter((i) => !i.inUse).length ?? 0,
      dangling: d?.images.filter((i) => !i.inUse && i.dangling).length ?? 0,
      volumes: d?.volumes.filter((v) => !v.inUse).length ?? 0,
      networks: d?.networks.filter((n) => !n.inUse && !n.builtin).length ?? 0,
    };
  });

  // Cleanup schedule form, in local time.
  protected readonly schedule = signal<CleanupScheduleDto | null>(null);
  protected readonly frequency = signal<CleanupFrequency>('Off');
  protected readonly day = signal(0);
  protected readonly hour = signal(3);
  protected readonly pruneVolumes = signal(false);

  constructor() {
    inject(NodesService).apiNodesGet().subscribe({ next: (n) => this.nodes.set(n) });
    effect(() => {
      this.host();
      untracked(() => {
        void this.reload();
        void this.loadSchedule();
      });
    });
  }

  private get nodeId(): string | undefined {
    return this.host() || undefined;
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await firstValueFrom(this.api.getResources(this.nodeId)));
    } catch (err) {
      this.data.set(null);
      this.error.set(`Could not read ${this.hostName()}: ${(err as { error?: { detail?: string } })?.error?.detail ?? 'the host did not answer.'}`);
    } finally {
      this.loading.set(false);
    }
  }

  protected imageName(i: DockerImageDto): string {
    return i.tags.length ? i.tags.join(', ') : '<none>';
  }

  protected shortId(id: string): string {
    return id.replace(/^sha256:/, '').slice(0, 12);
  }

  protected size(value: unknown): string {
    return value === null || value === undefined ? '–' : formatBytes(value);
  }

  protected count(value: unknown): number {
    return num(value);
  }

  protected async remove(kind: ResourceKind, id: string, name: string): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${kind.toLowerCase()} ${name}?`,
      message: kind === 'Volume' ? 'The volume and all data in it are deleted for good.' : `The ${kind.toLowerCase()} is removed from ${this.hostName()}.`,
      confirmText: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await this.run(async () => {
      await firstValueFrom(this.api.removeResource(kind, id, this.nodeId, name));
      this.notifications.success(`Deleted ${name}`);
    }, `Failed to delete ${name}`);
  }

  protected removeImage(i: DockerImageDto): Promise<void> {
    return this.remove('Image', i.id, i.tags[0] ?? this.shortId(i.id));
  }

  protected removeVolume(v: DockerVolumeDto): Promise<void> {
    return this.remove('Volume', v.name, v.name);
  }

  protected removeNetwork(n: DockerNetworkDto): Promise<void> {
    return this.remove('Network', n.id, n.name);
  }

  protected async pruneImages(all: boolean): Promise<void> {
    const ok = await this.confirm.ask({
      title: all ? 'Delete all unused images?' : 'Delete dangling images?',
      message: all
        ? `Removes every image no container on ${this.hostName()} uses (${this.unused().images}). They are pulled or built again when needed.`
        : `Removes untagged images no container uses (${this.unused().dangling}), typically left over from rebuilds.`,
      confirmText: 'Delete',
      destructive: true,
    });
    if (ok) await this.prune({ images: true, allImages: all });
  }

  protected async pruneVolumesNow(): Promise<void> {
    // Volumes hold data, so this needs a second, explicit tick on top of the confirm.
    const answer = await this.confirm.askWithOption({
      title: 'Delete all unused volumes?',
      message: `Deletes the ${this.unused().volumes} volumes no container on ${this.hostName()} uses, including named volumes of removed projects. Their data cannot be recovered.`,
      option: 'I understand the data in these volumes is deleted for good',
      confirmText: 'Delete volumes',
      destructive: true,
    });
    if (!answer) return;
    if (!answer.checked) {
      this.notifications.warning('Nothing deleted: tick the box to confirm deleting volume data.');
      return;
    }
    await this.prune({ volumes: true });
  }

  protected async pruneNetworks(): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete unused networks?',
      message: `Removes the ${this.unused().networks} networks no container on ${this.hostName()} is attached to. Built-in networks stay.`,
      confirmText: 'Delete',
      destructive: true,
    });
    if (ok) await this.prune({ networks: true });
  }

  private async prune(request: PruneRequest): Promise<void> {
    await this.run(async () => {
      const r = await firstValueFrom(this.api.pruneResources(request, this.nodeId));
      this.notifications.success(this.describe(r));
    }, 'Prune failed');
  }

  private describe(r: PruneResultDto): string {
    const parts = [
      [num(r.imagesDeleted), 'images'],
      [num(r.volumesDeleted), 'volumes'],
      [num(r.networksDeleted), 'networks'],
      [num(r.containersDeleted), 'containers'],
    ].filter(([n]) => n).map(([n, what]) => `${n} ${what}`);
    return `Removed ${parts.length ? parts.join(', ') : 'nothing'}, freed ${formatBytes(r.spaceReclaimed)}`;
  }

  private async run(work: () => Promise<void>, failure: string): Promise<void> {
    this.busy.set(true);
    try {
      await work();
    } catch (err) {
      this.notifications.showError(failure, err);
    } finally {
      this.busy.set(false);
      await this.reload();
    }
  }

  // ---- Scheduled cleanup ----

  private async loadSchedule(): Promise<void> {
    try {
      this.applySchedule(await firstValueFrom(this.api.getCleanupSchedule(this.nodeId)));
    } catch {
      this.schedule.set(null);
    }
  }

  private applySchedule(s: CleanupScheduleDto): void {
    this.schedule.set(s);
    const local = utcToLocal(num(s.dayOfWeekUtc), num(s.hourUtc));
    this.frequency.set(s.frequency);
    this.day.set(local.day);
    this.hour.set(local.hour);
    this.pruneVolumes.set(s.pruneVolumes);
  }

  protected async saveSchedule(): Promise<void> {
    const utc = localToUtc(this.day(), this.hour());
    this.busy.set(true);
    try {
      this.applySchedule(
        await firstValueFrom(
          this.api.saveCleanupSchedule({
            nodeId: this.nodeId ?? null,
            frequency: this.frequency(),
            hourUtc: utc.hour,
            dayOfWeekUtc: utc.day,
            pruneVolumes: this.pruneVolumes(),
          }),
        ),
      );
      this.notifications.success('Cleanup schedule saved');
    } catch (err) {
      this.notifications.showError('Failed to save the schedule', err);
    } finally {
      this.busy.set(false);
    }
  }

  protected async runCleanupNow(): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Clean up ${this.hostName()} now?`,
      message:
        'Removes stopped containers that belong to no compose project, unused networks, and images no container uses' +
        (this.schedule()?.pruneVolumes ? ', and unused volumes (their data is lost).' : '. Volumes are kept.'),
      confirmText: 'Clean up',
      destructive: true,
    });
    if (!ok) return;
    await this.run(async () => {
      const r = await firstValueFrom(this.api.runCleanup(this.nodeId));
      this.notifications.success(this.describe(r));
      await this.loadSchedule();
    }, 'Cleanup failed');
  }
}
