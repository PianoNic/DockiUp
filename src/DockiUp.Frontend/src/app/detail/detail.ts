import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ProjectStore } from '../shared/stores/project.store';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { ContainerDto, ContainerService, NodeDto, NodesService } from '../api';
import { UpdateMethodType, containerStateLabel, normalizeContainerState } from '../shared/models/api-enums';
import { NotificationService } from '../shared/services/notification.service';
import { ProjectDeployments } from './project-deployments';
import { ProjectSettings } from './project-settings';
import { ProjectFiles } from './project-files';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { MatTabsModule } from '@angular/material/tabs';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { ProjectImages } from './project-images';
import { ImageUpdateBadge } from '../shared/components/image-update-badge/image-update-badge';
import { ContainerLogs } from './container-logs';
import { ContainerStatsMeter } from '../shared/components/container-stats/container-stats-meter';
import { ContainerStatsHistory } from '../shared/components/container-stats/container-stats-history';

@Component({
  selector: 'app-detail',
  imports: [
    MatCardModule,
    MatInputModule,
    MatSelectModule,
    MatFormFieldModule,
    FormsModule,
    MatButtonModule,
    MatTooltipModule,
    MatIconModule,
    MatProgressSpinnerModule,
    RouterLink,
    ProjectDeployments,
    ProjectSettings,
    ProjectFiles,
    MatTabsModule,
    LocalDatePipe,
    ProjectImages,
    ImageUpdateBadge,
    ContainerLogs,
    ContainerStatsMeter,
    ContainerStatsHistory,
  ],
  templateUrl: './detail.html',
  styleUrl: './detail.scss',
})
export class Detail implements OnInit {
  private route = inject(ActivatedRoute);
  projectStore = inject(ProjectStore);
  private containerService = inject(ContainerService);
  private readonly nodes = signal<NodeDto[]>([]);
  /** Host name for the header eyebrow. */
  readonly hostName = computed(() => {
    const id = this.project()?.nodeId;
    return id ? (this.nodes().find((n) => n.id === id)?.name ?? 'node') : 'Local host';
  });
  private notifications = inject(NotificationService);
  private confirm = inject(ConfirmService);
  private router = inject(Router);
  readonly dockerId = this.route.snapshot.paramMap.get('id');

  /** Empty string = all containers, otherwise single container id. */
  readonly selectedContainerId = signal<string>(this.route.snapshot.queryParamMap.get('container') ?? '');

  UpdateMethodType = UpdateMethodType;
  stateLabel = containerStateLabel;

  constructor() {
    inject(NodesService).apiNodesGet().subscribe({ next: (n) => this.nodes.set(n), error: () => {} });
  }

  async ngOnInit() {
    await this.projectStore.loadContainers();
  }

  project = computed(() =>
    this.projectStore.projectDtos().find((a) => a.dockerProjectName === this.dockerId)
  );

  async lifecycle(action: 'start' | 'stop' | 'restart') {
    const p = this.project();
    if (p) await this.projectStore.lifecycle(p, action);
  }

  readonly pulling = signal(false);

  async pull() {
    const id = this.project()?.id;
    if (!id) return;
    this.pulling.set(true);
    try {
      await this.projectStore.pullImages(id);
    } finally {
      this.pulling.set(false);
    }
  }

  /** Clicking a container card shows only its logs; clicking it again goes back to all containers. */
  toggleContainerLogs(container: ContainerDto) {
    this.selectedContainerId.set(this.selectedContainerId() === container.id ? '' : container.id);
  }

  async removeProject() {
    const p = this.project();
    if (!p) return;
    const answer = await this.confirm.askWithOption({
      title: `Remove ${p.projectName}?`,
      message: p.managedByDockiUp
        ? 'Stops and deletes its containers and networks, and removes the project from DockiUp (its folder and deployment history).'
        : 'Stops and deletes its containers and networks (docker compose down). Its compose files are not touched.',
      option: 'Also delete its volumes (this deletes their data)',
      confirmText: 'Remove',
      destructive: true,
    });
    if (answer && (await this.projectStore.removeProject(p, answer.checked))) await this.router.navigate(['/dashboard']);
  }

  async onRemoveContainer(container: ContainerDto) {
    const ok = await this.confirm.ask({
      title: `Remove ${container.name}?`,
      message: 'The container is force-removed. A compose project recreates it on its next deploy or start.',
      confirmText: 'Remove',
      destructive: true,
    });
    if (ok) await this.projectStore.removeContainer(container, this.nodeId);
  }

  /** 0 Containers, 1 Deployments, 2 Images, 3 Files, 4 Settings. */
  readonly tab = signal(0);

  async deployNow() {
    const id = this.project()?.id;
    if (id && (await this.projectStore.deployNow(id))) this.tab.set(1); // watch it run
  }

  /** The node the current project runs on (undefined = local control-plane host). */
  private get nodeId(): string | undefined {
    return this.project()?.nodeId ?? undefined;
  }

  async onStartContainer(container: ContainerDto) {
    try {
      await firstValueFrom(this.containerService.startContainer(container.id, this.nodeId));
      await this.projectStore.loadContainers();
    } catch (err) {
      this.notifications.showError('Failed to start container', err);
    }
  }

  async onStopContainer(container: ContainerDto) {
    try {
      await firstValueFrom(this.containerService.stopContainer(container.id, this.nodeId));
      await this.projectStore.loadContainers();
    } catch (err) {
      this.notifications.showError('Failed to stop container', err);
    }
  }

  async onRestartContainer(container: ContainerDto) {
    try {
      await firstValueFrom(this.containerService.restartContainer(container.id, this.nodeId));
      await this.projectStore.loadContainers();
    } catch (err) {
      this.notifications.showError('Failed to restart container', err);
    }
  }

  getRunningCount(): number {
    return (
      (this.project()?.containers ?? []).filter(
        (c) => normalizeContainerState(c.state) === UpdateMethodType.Running
      ).length
    );
  }

  /** Every container is up: Start has nothing to do (Stop / Restart need at least one running). */
  allRunning(): boolean {
    const total = this.project()?.containers?.length ?? 0;
    return total > 0 && this.getRunningCount() === total;
  }

  /** Normalized state (API may send enum as string). */
  getContainerState(container: ContainerDto): number {
    return normalizeContainerState(container.state);
  }

  getStatusIcon(state: number | string | undefined): string {
    const n = normalizeContainerState(state);
    switch (n) {
      case UpdateMethodType.Running:
        return 'check_circle';
      case UpdateMethodType.Stopped:
        return 'stop';
      case UpdateMethodType.Updating:
        return 'loop';
      case UpdateMethodType.Crashed:
        return 'error';
      case UpdateMethodType.Created:
        return 'pending';
      default:
        return 'help';
    }
  }

  /** Compose services of the project's containers, for the Images tab. */
  readonly serviceNames = computed(() => [...new Set((this.project()?.containers ?? []).map((c) => c.serviceName).filter((s) => !!s))]);

  /** Managed projects have a DockiUp id: deploy pipeline, history and webhook. */
  readonly managedId = computed(() => (this.project()?.managedByDockiUp ? this.project()?.id ?? null : null));
}
