import { Component, OnInit, inject, computed, signal, effect, untracked } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { DashboardService, NodeDto, NodesService, ProjectDto } from '../api';
import { UpdateMethodType, normalizeContainerState } from '../shared/models/api-enums';
import { CreateProjectButton } from '../shared/components/create-project-button/button/create-project-button';
import { ProjectStore } from '../shared/stores/project.store';
import { ImageUpdateStore, updatesTooltip } from '../shared/stores/image-update.store';

type Filter = 'all' | 'attention' | 'updates';

interface HostGroup {
  /** Containers' host filter value: "Local host" or the node's name. */
  name: string;
  online: boolean;
  dockerVersion: string | null;
  total: number;
  projects: ProjectDto[];
}

/** Overview: container totals, then every project as a small card, grouped by host, problems first. */
@Component({
  selector: 'app-dashboard',
  imports: [MatIconModule, RouterLink, MatButtonModule, MatTooltipModule, CreateProjectButton, MatProgressBar, MatProgressSpinnerModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  projectStore = inject(ProjectStore);
  protected readonly imageUpdates = inject(ImageUpdateStore);
  private dashboardService = inject(DashboardService);
  private nodesService = inject(NodesService);

  projects = this.projectStore.projectDtos;
  readonly nodes = signal<NodeDto[]>([]);
  readonly localDockerVersion = signal<string | null>(null);

  protected readonly search = signal('');
  protected readonly filter = signal<Filter>('all');
  /** Host groups folded away by the user (by name). */
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());

  constructor() {
    // The project list live-updates over SignalR; refresh the host info (online, Docker version) with it.
    effect(() => {
      this.projects();
      untracked(() => this.loadOverview());
    });
  }

  async ngOnInit() {
    void this.imageUpdates.ensureLoaded();
    await this.projectStore.loadContainers();
  }

  async loadOverview() {
    try {
      const [stats, nodes] = await Promise.all([
        firstValueFrom(this.dashboardService.apiDashboardStatsGet()),
        firstValueFrom(this.nodesService.apiNodesGet()),
      ]);
      this.localDockerVersion.set(stats?.localDockerVersion ?? null);
      this.nodes.set(nodes ?? []);
    } catch {
      // Host details are best-effort; the project list is the primary content.
    }
  }

  // ---- totals ----
  readonly containerStats = computed(() => {
    const c = this.projects().flatMap((p) => p.containers || []);
    const by = (s: UpdateMethodType) => c.filter((x) => normalizeContainerState(x.state) === s).length;
    return { total: c.length, running: by(UpdateMethodType.Running), stopped: by(UpdateMethodType.Stopped), crashed: by(UpdateMethodType.Crashed) };
  });

  readonly attentionCount = computed(() => this.projects().filter((p) => this.needsAttention(p)).length);
  readonly updateCount = computed(() => this.projects().filter((p) => this.hasUpdate(p)).length);

  // ---- grouping ----
  readonly groups = computed<HostGroup[]>(() => {
    const q = this.search().trim().toLowerCase();
    const filter = this.filter();
    const shown = (p: ProjectDto) =>
      (!q || [p.projectName, p.dockerProjectName, p.projectDescription].some((v) => v?.toLowerCase().includes(q)))
      && (filter === 'all' || (filter === 'attention' ? this.needsAttention(p) : this.hasUpdate(p)));
    // Problems first, then updates, then by name.
    const rank = (p: ProjectDto) => (this.needsAttention(p) ? 0 : this.hasUpdate(p) ? 1 : 2);
    const sort = (list: ProjectDto[]) =>
      list.filter(shown).sort((a, b) => rank(a) - rank(b) || a.projectName.localeCompare(b.projectName));

    const all = this.projects();
    const local = all.filter((p) => !this.nodeId(p));
    const groups: HostGroup[] = [
      { name: 'Local host', online: true, dockerVersion: this.localDockerVersion(), total: local.length, projects: sort(local) },
      ...this.nodes().map((n) => {
        const mine = all.filter((p) => this.nodeId(p) === n.id);
        return { name: n.name, online: n.online, dockerVersion: n.dockerVersion ?? null, total: mine.length, projects: sort(mine) };
      }),
    ];
    // Hosts without projects only matter on the Nodes page; a filter hides the groups it empties.
    return groups.filter((g) => g.projects.length > 0);
  });

  protected toggle(host: string): void {
    this.collapsed.update((set) => {
      const next = new Set(set);
      if (!next.delete(host)) next.add(host);
      return next;
    });
  }

  // ---- project helpers ----
  private nodeId(project: ProjectDto): string | null {
    return (project as ProjectDto & { nodeId?: string | null }).nodeId ?? null;
  }

  getRunningCount(project: ProjectDto): number {
    return (project.containers || []).filter((c) => normalizeContainerState(c.state) === UpdateMethodType.Running).length;
  }

  getTotalCount(project: ProjectDto): number {
    return (project.containers || []).length;
  }

  /** Something isn't running that should, or crashed. */
  needsAttention(project: ProjectDto): boolean {
    return this.getStatus(project) === 'crashed' || this.getRunningCount(project) < this.getTotalCount(project);
  }

  hasUpdate(project: ProjectDto): boolean {
    return this.imageUpdates.updatesFor(project.id).length > 0;
  }

  updateTooltip(project: ProjectDto): string {
    return updatesTooltip(this.imageUpdates.updatesFor(project.id));
  }

  /** Drives the status dot's colour. */
  getStatus(project: ProjectDto): string {
    const states = (project?.containers ?? []).map((c) => normalizeContainerState(c.state));
    if (states.length === 0) return 'unknown';
    if (states.some((s) => s === UpdateMethodType.Crashed)) return 'crashed';
    if (states.some((s) => s === UpdateMethodType.Updating)) return 'updating';
    if (states.every((s) => s === UpdateMethodType.Stopped)) return 'stopped';
    if (states.some((s) => s !== UpdateMethodType.Running)) return 'partial';
    return 'running';
  }

  // ---- project actions ----
  async onStartProject(project: ProjectDto) {
    await this.projectStore.lifecycle(project, 'start');
  }

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
