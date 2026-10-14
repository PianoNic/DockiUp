import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { GitCredentialDto, GitCredentialsService, NotificationsService, ProjectChannelDto, ProjectSettingsDto, ProjectSettingsService } from '../api';
import { ChannelDialog, ChannelDialogData, EVENTS } from '../settings/channel-dialog';
import { NotificationService } from '../shared/services/notification.service';
import { ProjectWebhook } from './project-webhook';

/** The project's Settings tab: push webhook, notification channels and git credential (private repos). Variables live in the Environment tab. */
@Component({
  selector: 'app-project-settings',
  imports: [RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatSlideToggleModule, MatTooltipModule, ProjectWebhook],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './project-deployments.scss',
  styles: `
    .settings-stack { display: flex; flex-direction: column; gap: 16px; height: 100%; overflow: auto; }
    .settings-stack > app-project-webhook { height: auto; }
    .settings-stack > .panel { flex-shrink: 0; }
    .credential { max-width: 420px; }
    .hint a { color: var(--mat-sys-primary); }
    .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .head .section-title { flex: 1; margin: 0; }
    .channel { display: flex; align-items: center; gap: 12px; padding: 10px 4px; border-top: 1px solid var(--mat-sys-outline-variant); }
    .channel .main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
    .channel .name { font-weight: 600; }
    .channel .meta { font-size: 13px; color: var(--mat-sys-on-surface-variant); }
  `,
  template: `
    <div class="settings-stack">
      <app-project-webhook [projectId]="projectId()" />

      <section class="panel x-card">
        <div class="head">
          <h2 class="section-title"><span class="x-shape secondary sm"><mat-icon>notifications</mat-icon></span> Notifications</h2>
          <button matButton="tonal" (click)="addChannel()"><mat-icon>add</mat-icon> Add channel for this project</button>
        </div>
        <p class="hint">Which channels hear about this project's deployments and image updates. Channels for every project are managed in <a routerLink="/settings">Settings</a>.</p>
        @for (c of channels(); track c.id) {
          <div class="channel">
            <span class="main">
              <span class="name">{{ c.name }}</span>
              <span class="meta">{{ c.type }} · {{ c.allProjects ? 'every project' : 'picked projects' }}@if (!c.enabled) { · turned off }@if (c.events.length) { · {{ eventsText(c) }} }</span>
            </span>
            <mat-slide-toggle [checked]="c.selected" [disabled]="c.allProjects"
              [matTooltip]="c.allProjects ? 'This channel covers every project' : ''"
              (change)="setChannel(c, $event.checked)" [attr.aria-label]="'Notify ' + c.name" />
          </div>
        } @empty {
          <p class="hint">No channels yet.</p>
        }
      </section>

      @if (settings()?.isGit) {
        <section class="panel x-card">
          <h2 class="section-title"><span class="x-shape tertiary sm"><mat-icon>key</mat-icon></span> Repository access</h2>
          <p class="hint">Private repositories need a git credential for clone and fetch (also on nodes). Manage them in <a routerLink="/settings">Settings</a>.</p>
          <mat-form-field appearance="fill" class="credential" subscriptSizing="dynamic">
            <mat-label>Git credential</mat-label>
            <mat-select [value]="settings()?.gitCredentialId ?? ''" (selectionChange)="setCredential($event.value)">
              <mat-option value="">None (public repository)</mat-option>
              @for (c of credentials(); track c.id) { <mat-option [value]="c.id">{{ c.name }} ({{ c.username }})</mat-option> }
            </mat-select>
          </mat-form-field>
        </section>
      }

    </div>
  `,
})
export class ProjectSettings {
  readonly projectId = input.required<string>();

  private readonly api = inject(ProjectSettingsService);
  private readonly credentialsApi = inject(GitCredentialsService);
  private readonly toast = inject(NotificationService);
  private readonly notificationsApi = inject(NotificationsService);
  private readonly dialog = inject(MatDialog);

  protected readonly settings = signal<ProjectSettingsDto | null>(null);
  protected readonly credentials = signal<GitCredentialDto[]>([]);
  protected readonly channels = signal<ProjectChannelDto[]>([]);

  constructor() {
    effect(() => {
      const id = this.projectId();
      firstValueFrom(this.api.getProjectSettings(id))
        .then((s) => this.apply(s))
        .catch((err) => this.toast.showError('Failed to load project settings', err));
      this.loadChannels(id);
    });
    this.credentialsApi.listGitCredentials().subscribe({ next: (c) => this.credentials.set(c), error: () => this.credentials.set([]) });
  }

  private loadChannels(id: string): void {
    this.notificationsApi.listProjectNotificationChannels(id).subscribe({
      next: (c) => this.channels.set(c),
      error: (err) => this.toast.showError('Failed to load notification channels', err),
    });
  }

  protected eventsText(c: ProjectChannelDto): string {
    return c.events.map((e) => EVENTS.find((x) => x.value === e)?.label ?? e).join(', ');
  }

  protected async setChannel(c: ProjectChannelDto, selected: boolean): Promise<void> {
    try {
      await firstValueFrom(this.notificationsApi.setProjectNotificationChannel(this.projectId(), c.id, selected));
      this.toast.success(selected ? `${c.name} will notify about this project` : `${c.name} no longer notifies about this project`);
    } catch (err) {
      this.toast.showError(`Could not change ${c.name}`, err);
    }
    this.loadChannels(this.projectId());
  }

  protected addChannel(): void {
    this.dialog.open<ChannelDialog, ChannelDialogData>(ChannelDialog, { data: { forProject: this.projectId() }, width: '560px', maxWidth: '95vw' })
      .afterClosed().subscribe((saved) => saved && this.loadChannels(this.projectId()));
  }

  private apply(s: ProjectSettingsDto): void {
    this.settings.set(s);
  }

  protected async setCredential(id: string): Promise<void> {
    try {
      this.settings.set(await firstValueFrom(this.api.setProjectGitCredential(this.projectId(), { gitCredentialId: id || null })));
      this.toast.success('Git credential saved; it applies on the next fetch');
    } catch (err) {
      this.toast.showError('Could not change the git credential', err);
    }
  }
}
