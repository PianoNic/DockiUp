import { Component, computed, inject, OnInit, signal, ViewChild, ElementRef, afterNextRender, effect } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
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
import { ContainerDto, ContainerService, Configuration } from '../api';
import { UpdateMethodType, containerStateLabel, normalizeContainerState } from '../shared/models/api-enums';
import { NotificationService, errorMessage } from '../shared/services/notification.service';
import { ProjectDeployments } from './project-deployments';
import { ProjectSettings } from './project-settings';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { MatTabsModule } from '@angular/material/tabs';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';

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
    MatTabsModule,
    LocalDatePipe,
  ],
  templateUrl: './detail.html',
  styleUrl: './detail.scss',
})
export class Detail implements OnInit {
  private route = inject(ActivatedRoute);
  private http = inject(HttpClient);
  private configuration = inject(Configuration);
  projectStore = inject(ProjectStore);
  private containerService = inject(ContainerService);
  private notifications = inject(NotificationService);
  private confirm = inject(ConfirmService);
  private router = inject(Router);
  readonly dockerId = this.route.snapshot.paramMap.get('id');

  /** Empty string = all containers, otherwise single container id. */
  readonly selectedContainerId = signal<string>(this.route.snapshot.queryParamMap.get('container') ?? '');
  readonly consoleLogs = signal<string>('');
  readonly logsLoading = signal<boolean>(false);

  @ViewChild('logContainer') logContainerRef?: ElementRef<HTMLDivElement>;

  UpdateMethodType = UpdateMethodType;
  stateLabel = containerStateLabel;

  constructor() {
    afterNextRender(() => this.scrollLogsToBottom());
    effect(() => {
      this.consoleLogs();
      setTimeout(() => this.scrollLogsToBottom(), 0);
    });
  }

  async ngOnInit() {
    await this.projectStore.loadContainers();
    await this.loadConsoleLogs();
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
    void this.loadConsoleLogs();
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

  /** 0 Containers, 1 Deployments, 2 Webhook. */
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

  /** Managed projects have a DockiUp id: deploy pipeline, history and webhook. */
  readonly managedId = computed(() => (this.project()?.managedByDockiUp ? this.project()?.id ?? null : null));

  async loadConsoleLogs(): Promise<void> {
    const containerId = this.selectedContainerId();
    const p = this.project();
    const containers = p?.containers ?? [];

    this.logsLoading.set(true);
    try {
      const basePath = this.configuration.basePath ?? '';
      const url = `${basePath}/api/Container/GetContainerLogs`;
      const tail = 200;
      const nodeId = this.nodeId;

      if (!containerId && containers.length === 0) {
        this.consoleLogs.set('No containers in this project.');
        return;
      }

      if (!containerId && containers.length > 0) {
        // All containers: fetch in parallel and merge with container name prefix
        const results = await Promise.all(
          containers.map(async (c) => {
            try {
              const logs = await firstValueFrom(
                this.http.get(url, {
                  params: nodeId ? { containerId: c.id, tail, nodeId } : { containerId: c.id, tail },
                  responseType: 'text',
                })
              );
              return { name: c.name, logs: logs ?? '' };
            } catch (err) {
              this.notifications.showError(`Failed to load logs for ${c.name}`, err);
              return { name: c.name, logs: `(failed: ${errorMessage(err)})` };
            }
          })
        );
        const merged = results
          .map((r) => {
            const lines = (r.logs || '').split(/\r?\n/).filter((line) => line.length > 0);
            return lines.map((line) => `[${r.name}] ${line}`).join('\n');
          })
          .filter((block) => block.length > 0)
          .join('\n\n');
        this.consoleLogs.set(merged || '(no output from any container)');
      } else {
        const logs = await firstValueFrom(
          this.http.get(url, {
            params: nodeId ? { containerId, tail, nodeId } : { containerId, tail },
            responseType: 'text',
          })
        );
        this.consoleLogs.set(logs || '(no output)');
      }
    } catch (err) {
      const msg = errorMessage(err);
      this.consoleLogs.set('Failed to load logs. ' + msg);
      this.notifications.showError('Failed to load logs', err);
    } finally {
      this.logsLoading.set(false);
    }
  }

  scrollLogsToBottom(): void {
    const el = this.logContainerRef?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
