import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { debounceTime, firstValueFrom } from 'rxjs';
import { DeploymentTaskDto, ProjectService } from '../../api';
import { DockiUpHubService } from '../../shared/services/dockiup-hub.service';
import { LocalDatePipe } from '../../shared/pipes/local-date.pipe';

/** Header bell: deployments queued/running across all projects (badge), plus the latest finished ones. */
@Component({
  selector: 'app-task-center',
  imports: [RouterLink, MatBadgeModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, LocalDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .menu-head { padding: 8px 16px 4px; font: var(--mat-sys-title-small); }
    .empty { padding: 8px 16px 12px; color: var(--mat-sys-on-surface-variant); font-size: 13px; }
    .task { display: flex; align-items: center; gap: 10px; min-width: 300px; }
    .task-main { display: flex; flex-direction: column; min-width: 0; line-height: 1.3; }
    .task-name { font-weight: 500; }
    .task-sub { color: var(--mat-sys-on-surface-variant); font-size: 12px; }
    .spin { animation: spin 1.2s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `,
  template: `
    <button mat-icon-button [matMenuTriggerFor]="menu" (menuOpened)="refresh()" aria-label="Deployments"
      [matTooltip]="activeCount() ? activeCount() + ' deployment(s) in progress' : 'Deployments'"
      [matBadge]="activeCount() || null" matBadgeSize="small" matBadgeColor="accent">
      <mat-icon [class.spin]="activeCount() > 0">{{ activeCount() ? 'sync' : 'notifications' }}</mat-icon>
    </button>
    <mat-menu #menu="matMenu" xPosition="before">
      <div class="menu-head">Deployments</div>
      @for (t of tasks(); track t.deployment.id) {
        <a mat-menu-item [routerLink]="['/project', t.dockerProjectName]">
          <span class="task">
            <span class="state-badge" [attr.data-state]="badgeState(t)">{{ t.deployment.status.toLowerCase() }}</span>
            <span class="task-main">
              <span class="task-name">{{ t.projectName }}</span>
              <span class="task-sub">{{ t.deployment.trigger }} · {{ t.deployment.createdAt | localDate: 'short' }}</span>
            </span>
          </span>
        </a>
      } @empty {
        <div class="empty">Nothing deployed yet.</div>
      }
    </mat-menu>
  `,
})
export class TaskCenter {
  private readonly api = inject(ProjectService);

  protected readonly tasks = signal<DeploymentTaskDto[]>([]);
  protected readonly activeCount = computed(
    () => this.tasks().filter((t) => t.deployment.status === 'Queued' || t.deployment.status === 'Running').length,
  );

  constructor() {
    void this.refresh();
    // Any deployment change anywhere: refetch (names come with the list). Debounced for bursts.
    inject(DockiUpHubService)
      .deploymentChanged$.pipe(debounceTime(300), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => void this.refresh());
  }

  protected async refresh(): Promise<void> {
    try {
      this.tasks.set(await firstValueFrom(this.api.listDeploymentTasks(8 as never)));
    } catch {
      // The bell is secondary; a failed refresh keeps the last list rather than toasting on every event.
    }
  }

  protected badgeState(t: DeploymentTaskDto): string {
    return ({ Queued: 'pending', Running: 'updating', Succeeded: 'running', Failed: 'crashed' } as Record<string, string>)[t.deployment.status] ?? 'unknown';
  }
}
