import { Component, OnInit, inject, computed, signal, effect, untracked } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ActivityEntryDto, DashboardService, NodeDto, NodesService, ProjectDto } from '../api';
import { UpdateMethodType, normalizeContainerState } from '../shared/models/api-enums';
import { CreateProjectButton } from '../shared/components/create-project-button/button/create-project-button';
import { ProjectStore } from '../shared/stores/project.store';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { ImageUpdateBadge } from '../shared/components/image-update-badge/image-update-badge';
import { ImageUpdateStore } from '../shared/stores/image-update.store';

@Component({
  selector: 'app-dashboard',
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    RouterLink,
    LocalDatePipe,
    MatButtonModule,
    MatTooltipModule,
    CreateProjectButton,
    MatProgressBar,
    MatProgressSpinnerModule,
    ImageUpdateBadge,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  projectStore = inject(ProjectStore);
  protected readonly imageUpdates = inject(ImageUpdateStore);
  private dashboardService = inject(DashboardService);
  private nodesService = inject(NodesService);

  projects = this.projectStore.projectDtos;
  readonly recentActivity = signal<ActivityEntryDto[]>([]);
  readonly nodes = signal<NodeDto[]>([]);
  readonly localDockerVersion = signal<string | null>(null);

  constructor() {
    // The project list live-updates over SignalR (DockiUpHubService -> store). Whenever it changes,
    // refresh the overview panels (nodes + recent activity) so the whole dashboard stays current
    // without a manual refresh button.
    effect(() => {
      this.projects();
      untracked(() => this.loadOverview());
    });
  }

  async ngOnInit() {
    await this.projectStore.loadContainers();
  }

  async loadOverview() {
    try {
      const [stats, nodes] = await Promise.all([
        firstValueFrom(this.dashboardService.apiDashboardStatsGet()),
        firstValueFrom(this.nodesService.apiNodesGet()),
      ]);
      this.recentActivity.set(stats?.recentActivity ?? []);
      this.localDockerVersion.set(stats?.localDockerVersion ?? null);
      this.nodes.set(nodes ?? []);
    } catch {
      // Overview is best-effort; the project list is the primary content.
    }
  }

  // ---- KPI figures ----
  readonly allContainers = computed(() => this.projects().flatMap(p => p.containers || []));

  readonly containerStats = computed(() => {
    const c = this.allContainers();
    const by = (s: UpdateMethodType) => c.filter(x => normalizeContainerState(x.state) === s).length;
    return {
      total: c.length,
      running: by(UpdateMethodType.Running),
      stopped: by(UpdateMethodType.Stopped),
      updating: by(UpdateMethodType.Updating),
      crashed: by(UpdateMethodType.Crashed),
    };
  });

  /** Percentage widths for the health bar segments. */
  readonly health = computed(() => {
    const s = this.containerStats();
    const total = s.total || 1;
    return {
      running: (s.running / total) * 100,
      stopped: (s.stopped / total) * 100,
      crashed: (s.crashed / total) * 100,
      updating: (s.updating / total) * 100,
    };
  });

  readonly onlineNodeCount = computed(() => this.nodes().filter(n => n.online).length);

  projectCountForNode(nodeId: string): number {
    return this.projects().filter(p => (p as ProjectDto & { nodeId?: string }).nodeId === nodeId).length;
  }

  readonly localProjectCount = computed(() =>
    this.projects().filter(p => !(p as ProjectDto & { nodeId?: string }).nodeId).length);

  // ---- project helpers ----
  getRunningCount(project: ProjectDto): number {
    return (project.containers || []).filter(c => normalizeContainerState(c.state) === UpdateMethodType.Running).length;
  }

  getTotalCount(project: ProjectDto): number {
    return (project.containers || []).length;
  }

  getProjectStatusIcon(project: ProjectDto): string {
    const states = (project?.containers ?? []).map(c => normalizeContainerState(c.state));
    if (states.length === 0) return 'help';
    if (states.some(s => s === UpdateMethodType.Crashed)) return 'error';
    if (states.some(s => s === UpdateMethodType.Updating)) return 'loop';
    if (states.every(s => s === UpdateMethodType.Stopped)) return 'stop';
    if (states.some(s => s === UpdateMethodType.Running)) return 'check_circle';
    if (states.some(s => s === UpdateMethodType.Created)) return 'pending';
    return 'help';
  }

  /** CSS status class for a project (drives the accent colour). */
  getProjectStatusClass(project: ProjectDto): string {
    const states = (project?.containers ?? []).map(c => normalizeContainerState(c.state));
    if (states.length === 0) return 'unknown';
    if (states.some(s => s === UpdateMethodType.Crashed)) return 'crashed';
    if (states.some(s => s === UpdateMethodType.Updating)) return 'updating';
    if (states.every(s => s === UpdateMethodType.Stopped)) return 'stopped';
    if (states.some(s => s === UpdateMethodType.Running)) return 'running';
    return 'unknown';
  }

  projectNodeName(project: ProjectDto): string | null {
    const nodeId = (project as ProjectDto & { nodeId?: string }).nodeId;
    if (!nodeId) return null;
    return this.nodes().find(n => n.id === nodeId)?.name ?? 'node';
  }

  // ---- activity helpers ----
  activityIcon(action: string): string {
    if (action.startsWith('deploy')) return 'rocket_launch';
    if (action.startsWith('node.create')) return 'dns';
    if (action.startsWith('node.delete')) return 'delete';
    if (action.includes('stop')) return 'stop';
    if (action.includes('restart')) return 'refresh';
    if (action.includes('update')) return 'system_update';
    return 'bolt';
  }

  activityLabel(a: ActivityEntryDto): string {
    return `${a.action.replace(/[._]/g, ' ')} ${a.target}`.trim();
  }

  relativeTime(iso: string): string {
    const then = new Date(iso).getTime();
    if (Number.isNaN(then)) return '';
    const s = Math.max(0, Math.floor((Date.now() - then) / 1000));
    if (s < 60) return `${s}s ago`;
    const m = Math.floor(s / 60);
    if (m < 60) return `${m}m ago`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h}h ago`;
    return `${Math.floor(h / 24)}d ago`;
  }

  // ---- project actions ----
  async onStopProject(project: ProjectDto) {
    await this.projectStore.lifecycle(project, 'stop');
  }

  async onRestartProject(project: ProjectDto) {
    await this.projectStore.lifecycle(project, 'restart');
  }

  async onDeployProject(project: ProjectDto) {
    if (project.id) await this.projectStore.deployNow(project.id);
  }

  canDeploy(project: ProjectDto): boolean {
    return project.managedByDockiUp && !!project.id;
  }
}
