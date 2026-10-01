import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { GitCredentialDto, GitCredentialsService } from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { NotificationService, errorMessage } from '../shared/services/notification.service';

/** Add or edit a git credential. The token is write-only: leaving it empty on edit keeps the stored one. */
@Component({
  selector: 'app-git-credential-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `mat-form-field { width: 100%; } .error { color: var(--mat-sys-error); font-size: 14px; }`,
  template: `
    <h2 mat-dialog-title>{{ existing ? 'Edit git credential' : 'Add git credential' }}</h2>
    <mat-dialog-content>
      <p>An HTTPS personal access token with read access to the repository (GitHub, GitLab, Gitea/Forgejo).</p>
      <mat-form-field appearance="outline">
        <mat-label>Name</mat-label>
        <input matInput [(ngModel)]="name" placeholder="GitHub (my-org)" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>User name</mat-label>
        <input matInput [(ngModel)]="username" placeholder="git" />
        <mat-hint>Your account name; any value works for GitHub tokens</mat-hint>
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Token</mat-label>
        <input matInput type="password" autocomplete="new-password" [(ngModel)]="token"
          [placeholder]="existing ? 'Leave empty to keep ' + existing.tokenMasked : ''" />
        <mat-hint>Stored encrypted; never shown again</mat-hint>
      </mat-form-field>
      @if (error(); as err) { <p class="error">{{ err }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)" [disabled]="saving()">Cancel</button>
      <button mat-flat-button (click)="save()" [disabled]="saving() || !name.trim() || (!existing && !token)">{{ saving() ? 'Saving…' : 'Save' }}</button>
    </mat-dialog-actions>
  `,
})
export class GitCredentialDialog {
  protected readonly ref = inject<MatDialogRef<GitCredentialDialog, GitCredentialDto | null>>(MatDialogRef);
  private readonly api = inject(GitCredentialsService);
  protected readonly existing = inject<GitCredentialDto | null>(MAT_DIALOG_DATA);

  protected name = this.existing?.name ?? '';
  protected username = this.existing?.username ?? '';
  protected token = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async save(): Promise<void> {
    const request = { name: this.name.trim(), username: this.username.trim() || null, token: this.token || null };
    this.saving.set(true);
    this.error.set(null);
    try {
      this.ref.close(await firstValueFrom(this.existing
        ? this.api.updateGitCredential(this.existing.id, request)
        : this.api.createGitCredential(request)));
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }
}

/** Git credentials for private repositories; projects pick one at creation or in their Settings tab. */
@Component({
  selector: 'app-git-credentials',
  imports: [LocalDatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../activity/activity.scss',
  template: `
    <div class="activity-header">
      <p class="activity-subtitle">HTTPS tokens for cloning and fetching private repositories, also on nodes.</p>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon> Add credential</button>
    </div>
    @if (loading() && credentials().length === 0) { <mat-progress-bar mode="indeterminate" /> }
    @if (credentials().length === 0 && !loading()) {
      <p class="activity-empty">No git credentials yet. Public repositories need none.</p>
    } @else {
      <div class="table-scroll">
        <table class="activity-table">
          <thead><tr><th>Name</th><th>User</th><th>Token</th><th>Projects</th><th>Added</th><th></th></tr></thead>
          <tbody>
            @for (c of credentials(); track c.id) {
              <tr>
                <td class="target">{{ c.name }}</td>
                <td>{{ c.username }}</td>
                <td class="mono">{{ c.tokenMasked }}</td>
                <td>{{ count(c) }}</td>
                <td class="when">{{ c.createdAt | localDate }}</td>
                <td>
                  <div class="row-actions">
                    <button mat-icon-button (click)="edit(c)" matTooltip="Edit" aria-label="Edit credential"><mat-icon>edit</mat-icon></button>
                    <button mat-icon-button (click)="remove(c)" matTooltip="Delete" aria-label="Delete credential"><mat-icon>delete</mat-icon></button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class GitCredentials {
  private readonly api = inject(GitCredentialsService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(NotificationService);

  protected readonly credentials = signal<GitCredentialDto[]>([]);
  protected readonly loading = signal(false);

  constructor() {
    this.reload();
  }

  // The generator may type integers as an opaque wrapper (.NET emits integer|string).
  protected count = (c: GitCredentialDto) => Number(c.projectCount as unknown);

  private reload(): void {
    this.loading.set(true);
    this.api.listGitCredentials().subscribe({
      next: (c) => this.credentials.set(c),
      error: (err) => this.toast.showError('Failed to load git credentials', err),
    }).add(() => this.loading.set(false));
  }

  protected edit(credential: GitCredentialDto | null): void {
    this.dialog.open(GitCredentialDialog, { data: credential, width: '520px', maxWidth: '95vw' })
      .afterClosed().subscribe((saved) => saved && this.reload());
  }

  protected async remove(c: GitCredentialDto): Promise<void> {
    const ok = await this.confirm.ask({ title: 'Delete git credential?', message: `"${c.name}" will be removed from DockiUp.`, confirmText: 'Delete', destructive: true });
    if (!ok) return;
    this.api.deleteGitCredential(c.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.toast.showError('Could not delete the credential', err),
    });
  }
}
