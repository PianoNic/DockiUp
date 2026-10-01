import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
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
import { NgClass } from '@angular/common';

const TIME = new Intl.DateTimeFormat(undefined, { timeStyle: 'short' });
const DAY = new Intl.DateTimeFormat(undefined, { dateStyle: 'short' });

/** Rail bell: deployments queued/running across all projects (badge), plus the latest finished ones. */
@Component({
  selector: 'app-task-center',
  imports: [NgClass, RouterLink, MatBadgeModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .head { display: flex; align-items: flex-end; justify-content: space-between; gap: 12px; padding: 4px 8px 12px; }
    .head-text { display: flex; flex-direction: column; }
    .eyebrow { font-size: 12px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; color: var(--mat-sys-primary); }
    .title { font-size: 22px; font-weight: 800; color: var(--mat-sys-on-surface); }
    .rows { display: flex; flex-direction: column; gap: 3px; }
    .empty { padding: 20px 12px; border-radius: 24px; background: var(--mat-sys-surface-container); color: var(--mat-sys-on-surface-variant); font-size: 14px; }
    .task-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; line-height: 1.3; }
    .task-name { display: flex; align-items: center; gap: 8px; font-weight: 700; font-size: 15px; color: var(--mat-sys-on-surface); }
    .task-sub { color: var(--mat-sys-on-surface-variant); font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .task-sub.err { color: var(--mat-sys-error); }
    .when { color: var(--mat-sys-on-surface-variant); font-size: 13px; white-space: nowrap; }
    .state-badge { min-width: 0; height: 22px; padding: 0 10px; font-size: 12px; }
    .spin { animation: spin 1.2s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `,
  template: `
    @if (extended()) {
      <button type="button" class="rail-item" [matMenuTriggerFor]="menu" (menuOpened)="refresh()" aria-label="Deployments">
        <mat-icon [class.spin]="activeCount() > 0">{{ activeCount() ? 'sync' : 'notifications' }}</mat-icon>
        <span>Deployments</span>
        @if (activeCount()) { <span class="count">{{ activeCount() }}</span> }
      </button>
    } @else {
      <button type="button" class="rail-item" [matMenuTriggerFor]="menu" (menuOpened)="refresh()" aria-label="Deployments"
        [matTooltip]="activeCount() ? activeCount() + ' deployment(s) in progress' : 'Deployments'" matTooltipPosition="right"
        [matBadge]="activeCount() || null" matBadgeSize="small">
        <mat-icon [class.spin]="activeCount() > 0">{{ activeCount() ? 'sync' : 'notifications' }}</mat-icon>
      </button>
    }
    <mat-menu #menu="matMenu" class="x-deploy-menu" xPosition="after" yPosition="above">
      <div class="head" (click)="$event.stopPropagation()">
        <span class="head-text">
          <span class="eyebrow">{{ activeCount() ? activeCount() + ' in progress' : 'All done' }}</span>
          <span class="title">Deployments</span>
        </span>
        <a matButton routerLink="/activity">Activity<mat-icon iconPositionEnd>arrow_forward</mat-icon></a>
      </div>
      <div class="rows">
        @for (t of tasks(); track t.deployment.id) {
          <a mat-menu-item class="x-deploy-row" [routerLink]="['/project', t.dockerProjectName]">
            <span class="x-shape sm" [class.round]="t.deployment.status !== 'Queued'" [ngClass]="shapeClass(t)">
              <mat-icon [class.spin]="t.deployment.status === 'Running'">{{ statusIcon(t) }}</mat-icon>
            </span>
            <span class="task-main">
              <span class="task-name">
                {{ t.projectName }}
                @if (t.deployment.status === 'Running' || t.deployment.status === 'Queued') {
                  <span class="state-badge" [attr.data-state]="badgeState(t)">{{ t.deployment.status.toLowerCase() }}</span>
                }
              </span>
              <span class="task-sub" [class.err]="t.deployment.status === 'Failed'" [title]="subline(t)">{{ subline(t) }}</span>
            </span>
            <span class="when">{{ when(t.deployment.createdAt) }}</span>
          </a>
        } @empty {
          <div class="empty">Nothing deployed yet.</div>
        }
      </div>
    </mat-menu>
  `,
})
export class TaskCenter {
  /** Rail row with label; false = icon only (collapsed rail). */
  readonly extended = input(true);

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

  protected statusIcon(t: DeploymentTaskDto): string {
    return ({ Queued: 'schedule', Running: 'sync', Succeeded: 'check', Failed: 'priority_high' } as Record<string, string>)[t.deployment.status] ?? 'help';
  }

  protected shapeClass(t: DeploymentTaskDto): string {
    return ({ Running: 'tonal', Succeeded: 'ok', Failed: 'err' } as Record<string, string>)[t.deployment.status] ?? '';
  }

  /** Trigger, then the error for a failed run, otherwise the commit it deployed. */
  protected subline(t: DeploymentTaskDto): string {
    const d = t.deployment;
    const detail = d.status === 'Failed' ? d.error : d.status === 'Queued' ? 'queued' : d.status === 'Running' ? 'deploying now' : d.commitAfter?.slice(0, 7);
    return detail ? `${d.trigger} · ${detail}` : d.trigger;
  }

  /** Time for today's runs, the date for older ones (browser locale; the API sends UTC). */
  protected when(value: string): string {
    const date = new Date(value);
    const today = new Date().toDateString() === date.toDateString();
    return today ? TIME.format(date) : DAY.format(date);
  }

  protected badgeState(t: DeploymentTaskDto): string {
    return ({ Queued: 'pending', Running: 'updating', Succeeded: 'running', Failed: 'crashed' } as Record<string, string>)[t.deployment.status] ?? 'unknown';
  }
}
