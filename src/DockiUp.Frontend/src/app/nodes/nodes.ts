import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { interval } from 'rxjs';
import { NodeDto, NodesService } from '../api';
import { AddNodeDialog } from './add-node-dialog';
import { HostCard, HostState } from './host-card';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { errorMessage } from '../shared/services/notification.service';
import { ProjectStore } from '../shared/stores/project.store';
import { formatBytes, num } from '../shared/stores/container-stats.store';
import { HostStatsStore, containersPerHost, gpuTotals, hostKey } from '../shared/stores/host-stats.store';

interface HostEntry { nodeId: string | null; name: string; state: HostState; lastSeenAt: string | null; node: NodeDto | null }

/** Every host DockiUp deploys to, this one first, with its live usage. Add, ping and remove nodes here. */
@Component({
  selector: 'app-nodes',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, HostCard],
  templateUrl: './nodes.html',
  styleUrl: './nodes.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Nodes {
  private readonly api = inject(NodesService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly projectStore = inject(ProjectStore);
  protected readonly hostStats = inject(HostStatsStore);

  protected readonly nodes = signal<NodeDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly deleting = signal<Record<string, boolean>>({});

  protected readonly hosts = computed<HostEntry[]>(() => [
    { nodeId: null, name: this.hostStats.stats(null)?.hostname || 'This host', state: 'online', lastSeenAt: null, node: null },
    ...this.nodes().map((n) => ({
      nodeId: n.id,
      name: n.name,
      state: (n.online ? 'online' : n.pending ? 'pending' : 'offline') as HostState,
      lastSeenAt: n.pending ? null : n.lastSeenAt,
      node: n,
    })),
  ]);
  protected readonly containers = computed(() => containersPerHost(this.projectStore.projectDtos()));

  protected readonly totals = computed(() => {
    const live = this.hosts().map((h) => this.hostStats.stats(h.nodeId)).filter((s) => !!s);
    const online = this.hosts().filter((h) => h.state === 'online').length;
    const pending = this.hosts().filter((h) => h.state === 'pending').map((h) => h.name);
    const cores = live.reduce((s, h) => s + num(h.cores), 0);
    const cpu = cores ? Math.round(live.reduce((s, h) => s + num(h.cpuPercent) * num(h.cores), 0) / cores) : 0;
    const memUsed = live.reduce((s, h) => s + num(h.memoryUsed), 0);
    const memTotal = live.reduce((s, h) => s + num(h.memoryTotal), 0);
    const gpus = live.flatMap((h) => h.gpus ?? []);
    const gpuHosts = live.filter((h) => (h.gpus ?? []).length).length;
    const gpuLoad = gpuTotals(gpus).load;
    return [
      { label: 'Hosts online', value: `${online} / ${this.hosts().length}`, sub: pending.length ? `${pending.join(', ')} still pending` : this.hosts().map((h) => h.name).join(', ') },
      { label: 'CPU, all hosts', value: `${cpu}%`, sub: `${cores} cores in total` },
      { label: 'Memory, all hosts', value: `${formatBytes(memUsed)}`, sub: memTotal ? `of ${formatBytes(memTotal)} · ${Math.round((memUsed / memTotal) * 100)}% used` : '—' },
      { label: 'GPUs', value: `${gpus.length}`, sub: gpus.length ? `on ${gpuHosts} ${gpuHosts === 1 ? 'host' : 'hosts'}${gpuLoad != null ? ` · ${gpuLoad}% busy` : ''}` : 'none found' },
    ];
  });

  constructor() {
    this.hostStats.start();
    this.reload();
    // Nodes connect and drop in real time; refresh their state on a light cadence.
    interval(5000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => this.reload(true));
  }

  protected reload(silent = false): void {
    if (!silent) this.loading.set(true);
    this.api.apiNodesGet().subscribe({
      next: (nodes) => {
        this.nodes.set(nodes);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(errorMessage(err));
        this.loading.set(false);
      },
    });
  }

  protected containersOf(nodeId: string | null) {
    return this.containers().get(hostKey(nodeId)) ?? { running: 0, total: 0, projects: 0 };
  }

  protected addNode(): void {
    this.dialog
      .open(AddNodeDialog, { width: '720px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((created) => created && this.reload(true));
  }

  protected async remove(node: NodeDto): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Remove ${node.name}?`,
      message: 'DockiUp forgets this node and its token stops working. Its containers keep running on that host; stop the node container there afterwards.',
      confirmText: 'Remove node',
      destructive: true,
    });
    if (!ok) return;
    this.deleting.update((d) => ({ ...d, [node.id]: true }));
    this.api.apiNodesIdDelete(node.id).subscribe({
      next: () => this.reload(true),
      error: (err) => this.error.set(errorMessage(err)),
    }).add(() => this.deleting.update((d) => ({ ...d, [node.id]: false })));
  }
}
