import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { ContainerDto, ContainerService, ContainerStatsDto, NodeDto, NodesService } from '../api';
import { ContainerStatsStore, formatBytes, num } from '../shared/stores/container-stats.store';
import { ProjectStore } from '../shared/stores/project.store';
import { NotificationService } from '../shared/services/notification.service';
import { UpdateMethodType, containerStateLabel, normalizeContainerState } from '../shared/models/api-enums';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { ImageUpdateBadge } from '../shared/components/image-update-badge/image-update-badge';
import { ImageUpdateStore } from '../shared/stores/image-update.store';

type Row = ContainerDto & { dockerProjectName: string; nodeId: string | null; stateValue: number; projectId: string | null };

/** Every container across all projects and nodes, live from the project store (SignalR-fed). */
@Component({
  selector: 'app-containers',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, ImageUpdateBadge],
  templateUrl: './containers.html',
  styleUrls: ['../activity/activity.scss', './containers.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Containers {
  protected readonly store = inject(ProjectStore);
  private readonly containerService = inject(ContainerService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);
  protected readonly imageUpdates = inject(ImageUpdateStore);
  protected readonly stats = inject(ContainerStatsStore);

  protected readonly Running = UpdateMethodType.Running;
  protected readonly Stopped = UpdateMethodType.Stopped;
  protected readonly filter = signal('');
  private readonly nodes = signal<NodeDto[]>([]);

  protected readonly rows = computed<Row[]>(() =>
    this.store.projectDtos().flatMap((p) =>
      p.containers.map((c) => ({
        ...c,
        dockerProjectName: p.dockerProjectName,
        nodeId: (p as { nodeId?: string | null }).nodeId ?? null,
        stateValue: normalizeContainerState(c.state),
        projectId: p.id ?? null,
      })),
    ),
  );

  // Client-side filters over what is loaded: state, host, then the text filter.
  protected readonly stateFilter = signal<'all' | 'running' | 'stopped'>('all');
  // Preset by ?host=<name> (the dashboard's Hosts card links here).
  protected readonly hostFilter = signal<string | null>(inject(ActivatedRoute).snapshot.queryParamMap.get('host'));

  protected readonly hosts = computed(() => [...new Set(this.rows().map((r) => this.nodeName(r)))].sort());

  protected readonly stateFilters = computed(() => {
    const rows = this.rows();
    const running = rows.filter((r) => r.stateValue === UpdateMethodType.Running).length;
    return [
      { key: 'all' as const, label: 'All', count: rows.length },
      { key: 'running' as const, label: 'Running', count: running },
      { key: 'stopped' as const, label: 'Stopped', count: rows.length - running },
    ];
  });

  protected readonly filtered = computed(() => {
    const q = this.filter().toLowerCase().trim();
    const state = this.stateFilter();
    const host = this.hostFilter();
    return this.rows().filter((r) => {
      const running = r.stateValue === UpdateMethodType.Running;
      if (state === 'running' && !running) return false;
      if (state === 'stopped' && running) return false;
      if (host !== null && this.nodeName(r) !== host) return false;
      return !q || [r.name, r.dockerProjectName, r.serviceName, r.status, this.stateLabel(r), this.nodeName(r)]
        .some((v) => v?.toLowerCase().includes(q));
    });
  });

  // Containers clustered by compose project, so what runs together reads together.
  protected readonly groups = computed(() => {
    const byProject = new Map<string, Row[]>();
    for (const r of this.filtered()) {
      const key = `${r.nodeId ?? ''}/${r.dockerProjectName}`;
      byProject.set(key, [...(byProject.get(key) ?? []), r]);
    }
    return [...byProject].map(([key, rows]) => ({
      key,
      name: rows[0].dockerProjectName,
      projectId: rows[0].projectId,
      node: this.nodeName(rows[0]),
      running: rows.filter((r) => r.stateValue === UpdateMethodType.Running).length,
      rows,
    }));
  });

  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());

  protected toggle(key: string): void {
    this.collapsed.update((c) => {
      const next = new Set(c);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }

  protected readonly runningCount = computed(() => this.rows().filter((r) => r.stateValue === UpdateMethodType.Running).length);

  constructor() {
    void this.store.loadContainers();
    void this.stats.ensureLoaded();
    inject(NodesService).apiNodesGet().subscribe({ next: (n) => this.nodes.set(n) });
  }

  protected cpu(s: ContainerStatsDto): string {
    return `${num(s.cpuPercent).toFixed(1)}%`;
  }

  protected cpuPercent(s: ContainerStatsDto): number {
    return Math.min(100, Math.max(2, num(s.cpuPercent)));
  }

  protected memory(s: ContainerStatsDto): string {
    return formatBytes(s.memoryUsage);
  }

  protected stateLabel(r: Row): string {
    return containerStateLabel(r.stateValue);
  }

  protected nodeName(r: Row): string {
    return r.nodeId ? (this.nodes().find((n) => n.id === r.nodeId)?.name ?? 'node') : 'Local host';
  }

  /** Opens the container's project with this container's logs selected. */
  protected open(r: Row): void {
    void this.router.navigate(['/project', r.dockerProjectName], { queryParams: { container: r.id } });
  }

  protected async remove(r: Row): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Remove ${r.name}?`,
      message: 'The container is force-removed. A compose project recreates it on its next deploy or start.',
      confirmText: 'Remove',
      destructive: true,
    });
    if (ok) await this.store.removeContainer(r, r.nodeId);
  }

  protected async act(r: Row, action: 'start' | 'stop' | 'restart'): Promise<void> {
    const call = {
      start: () => this.containerService.startContainer(r.id, r.nodeId ?? undefined),
      stop: () => this.containerService.stopContainer(r.id, r.nodeId ?? undefined),
      restart: () => this.containerService.restartContainer(r.id, r.nodeId ?? undefined),
    }[action];
    try {
      await firstValueFrom(call());
      await this.store.loadContainers();
    } catch (err) {
      this.notifications.showError(`Failed to ${action} ${r.name}`, err);
    }
  }
}
