import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { interval } from 'rxjs';
import { NodeDto, NodesService } from '../api';
import { AddNodeDialog } from './add-node-dialog';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { errorMessage } from '../shared/services/notification.service';

/** Remote Docker hosts (KRINT-style): add, ping and remove nodes. */
@Component({
  selector: 'app-nodes',
  imports: [LocalDatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  templateUrl: './nodes.html',
  styleUrls: ['../activity/activity.scss', './nodes.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Nodes {
  private readonly api = inject(NodesService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);

  protected readonly nodes = signal<NodeDto[]>([]);
  protected readonly onlineCount = computed(() => this.nodes().filter((n) => n.online).length);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pinging = signal<Record<string, boolean>>({});
  protected readonly pingResults = signal<Record<string, string>>({});
  protected readonly deleting = signal<Record<string, boolean>>({});

  constructor() {
    this.reload();
    // Nodes connect/drop in real time, so refresh on a light cadence to keep status current.
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

  protected addNode(): void {
    this.dialog
      .open(AddNodeDialog, { width: '720px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((created) => created && this.reload(true));
  }

  protected async remove(node: NodeDto): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Remove node?',
      message: `"${node.name}" will be disconnected and its token will stop working.`,
      confirmText: 'Remove',
      destructive: true,
    });
    if (!ok) return;
    this.deleting.update((d) => ({ ...d, [node.id]: true }));
    this.api.apiNodesIdDelete(node.id).subscribe({
      next: () => this.reload(true),
      error: (err) => this.error.set(errorMessage(err)),
    }).add(() => this.deleting.update((d) => ({ ...d, [node.id]: false })));
  }

  protected ping(id: string): void {
    this.pinging.update((p) => ({ ...p, [id]: true }));
    this.api.apiNodesIdPingPost(id).subscribe({
      // roundTripMs is typed as an opaque wrapper by the generator (.NET emits integer|string).
      next: (r) => this.pingResults.update((m) => ({ ...m, [id]: `${r.reply} · ${Number(r.roundTripMs as unknown)}ms` })),
      error: () => this.pingResults.update((m) => ({ ...m, [id]: 'unreachable' })),
    }).add(() => this.pinging.update((p) => ({ ...p, [id]: false })));
  }
}
