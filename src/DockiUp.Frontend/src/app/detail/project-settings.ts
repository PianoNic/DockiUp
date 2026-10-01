import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { GitCredentialDto, GitCredentialsService, ProjectSettingsDto, ProjectSettingsService, SecretDto, SecretsService } from '../api';
import { SecretDialog } from '../settings/vault-secrets';
import { NotificationService } from '../shared/services/notification.service';
import { ProjectWebhook } from './project-webhook';

interface Row { envName: string; secretId: string }

/** The project's Settings tab: push webhook, git credential (private repos) and env vars from vault secrets. */
@Component({
  selector: 'app-project-settings',
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatTooltipModule, ProjectWebhook],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './project-deployments.scss',
  styles: `
    .settings-stack { display: flex; flex-direction: column; gap: 16px; height: 100%; overflow: auto; }
    .settings-stack > app-project-webhook { height: auto; }
    .settings-stack > .panel { flex-shrink: 0; }
    .row { display: flex; align-items: center; gap: 12px; }
    .row mat-form-field { flex: 1; min-width: 0; }
    .actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 4px; }
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

      <section class="panel x-card">
        <h2 class="section-title"><span class="x-shape sm"><mat-icon>lock</mat-icon></span> Secrets</h2>
        <p class="hint">
          Each variable is filled from the vault at deploy time and written, with the project's <code>.env</code>, to an env file
          compose reads (<code>--env-file</code>). Mapped secrets win over the same name in <code>.env</code>. Reference them in
          the compose file as <code>{{ example }}</code>. Values never appear in deployment logs.
        </p>
        @for (row of rows(); track $index) {
          <div class="row">
            <mat-form-field appearance="fill" subscriptSizing="dynamic">
              <mat-label>Variable</mat-label>
              <input matInput [(ngModel)]="row.envName" (ngModelChange)="dirty.set(true)" placeholder="DB_PASSWORD" />
            </mat-form-field>
            <mat-form-field appearance="fill" subscriptSizing="dynamic">
              <mat-label>Vault secret</mat-label>
              <mat-select [(ngModel)]="row.secretId" (ngModelChange)="dirty.set(true)">
                @for (s of vault(); track s.id) { <mat-option [value]="s.id">{{ s.name }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <button mat-icon-button (click)="removeRow($index)" matTooltip="Remove" aria-label="Remove variable"><mat-icon>delete</mat-icon></button>
          </div>
        } @empty {
          <p class="empty">No variables mapped.</p>
        }
        <div class="actions">
          <button matButton="tonal" (click)="addRow()"><mat-icon>add</mat-icon> Add variable</button>
          <button matButton="tonal" (click)="newSecret()"><mat-icon>enhanced_encryption</mat-icon> New vault secret</button>
          <button mat-flat-button (click)="save()" [disabled]="!dirty() || saving()">{{ saving() ? 'Saving…' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
})
export class ProjectSettings {
  readonly projectId = input.required<string>();

  private readonly api = inject(ProjectSettingsService);
  private readonly secretsApi = inject(SecretsService);
  private readonly credentialsApi = inject(GitCredentialsService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(NotificationService);

  protected readonly settings = signal<ProjectSettingsDto | null>(null);
  protected readonly vault = signal<SecretDto[]>([]);
  protected readonly credentials = signal<GitCredentialDto[]>([]);
  protected readonly rows = signal<Row[]>([]);
  protected readonly dirty = signal(false);
  protected readonly saving = signal(false);
  protected readonly example = '${DB_PASSWORD}';

  constructor() {
    effect(() => {
      const id = this.projectId();
      firstValueFrom(this.api.getProjectSettings(id))
        .then((s) => this.apply(s))
        .catch((err) => this.toast.showError('Failed to load project settings', err));
    });
    this.loadVault();
    this.credentialsApi.listGitCredentials().subscribe({ next: (c) => this.credentials.set(c), error: () => this.credentials.set([]) });
  }

  private apply(s: ProjectSettingsDto): void {
    this.settings.set(s);
    this.rows.set(s.secrets.map((m) => ({ envName: m.envName, secretId: m.secretId })));
    this.dirty.set(false);
  }

  private loadVault(): Promise<SecretDto[]> {
    return firstValueFrom(this.secretsApi.apiSecretsGet())
      .then((v) => { this.vault.set(v); return v; })
      .catch((err) => { this.toast.showError('Failed to load the vault', err); return []; });
  }

  protected addRow(): void {
    this.rows.update((r) => [...r, { envName: '', secretId: '' }]);
    this.dirty.set(true);
  }

  protected removeRow(index: number): void {
    this.rows.update((r) => r.filter((_, i) => i !== index));
    this.dirty.set(true);
  }

  // Create a secret without leaving the page, then map it in an empty row (or a new one).
  protected newSecret(): void {
    this.dialog.open(SecretDialog, { data: null, width: '520px', maxWidth: '95vw' }).afterClosed().subscribe(async (name) => {
      if (!name) return;
      const created = (await this.loadVault()).find((s) => s.name === name);
      if (!created) return;
      this.rows.update((r) => {
        const empty = r.findIndex((x) => !x.secretId);
        return empty >= 0
          ? r.map((x, i) => (i === empty ? { ...x, secretId: created.id } : x))
          : [...r, { envName: name.toUpperCase().replace(/[^A-Z0-9_]/g, '_').replace(/^(\d)/, '_$1'), secretId: created.id }];
      });
      this.dirty.set(true);
    });
  }

  protected async save(): Promise<void> {
    const mappings = this.rows().filter((r) => r.envName.trim() || r.secretId);
    if (mappings.some((r) => !r.envName.trim() || !r.secretId)) {
      this.toast.error('Every variable needs a name and a vault secret');
      return;
    }
    this.saving.set(true);
    try {
      this.apply(await firstValueFrom(this.api.setProjectSecrets(this.projectId(), mappings.map((r) => ({ envName: r.envName.trim(), secretId: r.secretId })))));
      this.toast.success(mappings.length ? 'Secrets saved; they apply on the next deploy' : 'Secrets removed');
    } catch (err) {
      this.toast.showError('Could not save secrets', err);
    } finally {
      this.saving.set(false);
    }
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
