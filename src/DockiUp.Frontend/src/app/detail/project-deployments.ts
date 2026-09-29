import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { DeploymentDto, ProjectService } from '../api';
import { DockiUpHubService } from '../shared/services/dockiup-hub.service';
import { NotificationService } from '../shared/services/notification.service';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';

/** Deployment history with live logs. */
@Component({
  selector: 'app-project-deployments',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, LocalDatePipe],
  templateUrl: './project-deployments.html',
  styleUrl: './project-deployments.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectDeployments {
  readonly projectId = input.required<string>();
  /** What's running now, so we don't offer to redeploy the version that's already live. */
  readonly deployedCommit = input<string | null>(null);

  private readonly api = inject(ProjectService);
  private readonly hub = inject(DockiUpHubService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly logBox = viewChild<ElementRef<HTMLElement>>('logBox');

  protected readonly deployments = signal<DeploymentDto[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly log = signal('');
  protected readonly selected = computed(() => this.deployments().find((d) => d.id === this.selectedId()) ?? null);

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });

    const destroyRef = inject(DestroyRef);
    this.hub.deploymentChanged$.pipe(takeUntilDestroyed(destroyRef)).subscribe((d) => this.onChanged(d));
    this.hub.deploymentLog$.pipe(takeUntilDestroyed(destroyRef)).subscribe(({ deploymentId, line }) => {
      if (deploymentId !== this.selectedId()) return;
      this.log.update((l) => l + line + '\n');
      this.scrollToEnd();
    });
  }

  private async load(projectId: string): Promise<void> {
    try {
      const list = await firstValueFrom(this.api.listDeployments(projectId));
      this.deployments.set(list);
      if (!this.selectedId() && list.length) await this.select(list[0]);
    } catch (err) {
      this.notifications.showError('Failed to load deployments', err);
    }
  }

  private onChanged(d: DeploymentDto): void {
    if (d.projectId !== this.projectId()) return;
    this.deployments.update((list) =>
      [d, ...list.filter((x) => x.id !== d.id)].sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
    );

    // Follow a deployment the moment it starts; when the followed one finishes, load its full log.
    if (d.status === 'Running' && this.selectedId() !== d.id) {
      this.selectedId.set(d.id);
      this.log.set('');
    } else if (d.id === this.selectedId() && this.isFinished(d)) {
      void this.select(d);
    }
  }

  protected async select(d: DeploymentDto): Promise<void> {
    this.selectedId.set(d.id);
    try {
      const full = await firstValueFrom(this.api.getDeployment(d.id));
      if (this.selectedId() === d.id) {
        this.log.set(full.log || (d.status === 'Queued' ? 'Waiting in the queue...' : ''));
        this.scrollToEnd();
      }
    } catch (err) {
      this.notifications.showError('Failed to load the deployment log', err);
    }
  }

  /** A finished, successful git deployment whose commit isn't the one running now can be redeployed. */
  protected canRedeploy(d: DeploymentDto): boolean {
    return d.status === 'Succeeded' && !!d.commitAfter && d.commitAfter !== this.deployedCommit();
  }

  protected async redeploy(d: DeploymentDto): Promise<void> {
    const sha = this.shortSha(d.commitAfter);
    const ok = await this.confirm.ask({
      title: `Deploy ${sha} again?`,
      message: `Checks out ${sha}${d.commitMessage ? ` ("${d.commitMessage}")` : ''} and redeploys it. `
        + 'A later push, webhook or periodic check moves the project forward to the branch tip again.',
      confirmText: 'Deploy this version',
    });
    if (!ok) return;
    try {
      await firstValueFrom(this.api.redeployVersion(d.id));
      this.notifications.success(`Deploying ${sha}`);
    } catch (err) {
      this.notifications.showError('Failed to queue the redeploy', err);
    }
  }

  // Reuse the global .state-badge palette: queued orange, running blue, succeeded green, failed red.
  protected badgeState(d: DeploymentDto): string {
    return ({ Queued: 'pending', Running: 'updating', Succeeded: 'running', Failed: 'crashed' } as Record<string, string>)[d.status] ?? 'unknown';
  }

  protected isFinished(d: DeploymentDto): boolean {
    return d.status === 'Succeeded' || d.status === 'Failed';
  }

  protected shortSha(sha?: string | null): string {
    return sha ? sha.slice(0, 7) : '';
  }

  protected duration(d: DeploymentDto): string {
    if (!d.startedAt) return '';
    const end = d.finishedAt ? new Date(d.finishedAt).getTime() : Date.now();
    const s = Math.max(0, Math.round((end - new Date(d.startedAt).getTime()) / 1000));
    return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`;
  }

  private scrollToEnd(): void {
    setTimeout(() => {
      const el = this.logBox()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }
}
