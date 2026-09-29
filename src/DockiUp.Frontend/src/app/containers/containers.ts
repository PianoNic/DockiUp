import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { ContainerDto, ContainerService, NodeDto, NodesService } from '../api';
import { ProjectStore } from '../shared/stores/project.store';
import { NotificationService } from '../shared/services/notification.service';
import { UpdateMethodType, containerStateLabel, normalizeContainerState } from '../shared/models/api-enums';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';

type Row = ContainerDto & { dockerProjectName: string; nodeId: string | null; stateValue: number };

/** Every container across all projects and nodes, live from the project store (SignalR-fed). */
@Component({
  selector: 'app-containers',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  templateUrl: './containers.html',
  styleUrl: '../activity/activity.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Containers {
  protected readonly store = inject(ProjectStore);
  private readonly containerService = inject(ContainerService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);

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
      })),
    ),
  );

  protected readonly filtered = computed(() => {
    const q = this.filter().toLowerCase().trim();
    if (!q) return this.rows();
    return this.rows().filter((r) =>
      [r.name, r.dockerProjectName, r.serviceName, r.status, this.stateLabel(r), this.nodeName(r)]
        .some((v) => v?.toLowerCase().includes(q)),
    );
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
    inject(NodesService).apiNodesGet().subscribe({ next: (n) => this.nodes.set(n) });
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
