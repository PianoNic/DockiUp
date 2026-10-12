import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { GitCredentialDto, GitCredentialsService, ProjectSettingsDto, ProjectSettingsService } from '../api';
import { NotificationService } from '../shared/services/notification.service';
import { ProjectWebhook } from './project-webhook';

/** The project's Settings tab: push webhook and git credential (private repos). Variables live in the Environment tab. */
@Component({
  selector: 'app-project-settings',
  imports: [RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule, ProjectWebhook],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './project-deployments.scss',
  styles: `
    .settings-stack { display: flex; flex-direction: column; gap: 16px; height: 100%; overflow: auto; }
    .settings-stack > app-project-webhook { height: auto; }
    .settings-stack > .panel { flex-shrink: 0; }
    .credential { max-width: 420px; }
    .hint a { color: var(--mat-sys-primary); }
  `,
  template: `
    <div class="settings-stack">
      <app-project-webhook [projectId]="projectId()" />

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

  protected readonly settings = signal<ProjectSettingsDto | null>(null);
  protected readonly credentials = signal<GitCredentialDto[]>([]);

  constructor() {
    effect(() => {
      const id = this.projectId();
      firstValueFrom(this.api.getProjectSettings(id))
        .then((s) => this.apply(s))
        .catch((err) => this.toast.showError('Failed to load project settings', err));
    });
    this.credentialsApi.listGitCredentials().subscribe({ next: (c) => this.credentials.set(c), error: () => this.credentials.set([]) });
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
