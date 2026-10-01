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
import { SecretDto, SecretsService } from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { NotificationService, errorMessage } from '../shared/services/notification.service';

/** Stores a vault secret. An empty value generates one, shown exactly once. Closes with the secret's name. */
@Component({
  selector: 'app-secret-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    mat-form-field { width: 100%; }
    .error { color: var(--mat-sys-error); font-size: 14px; }
    .generated { display: flex; align-items: center; gap: 8px; padding: 8px 12px; border-radius: 10px; background: var(--mat-sys-surface-container);
      code { flex: 1; overflow-wrap: anywhere; } }
  `,
  template: `
    <h2 mat-dialog-title>{{ generated() ? 'Secret generated' : (overwrite ? 'Replace secret value' : 'Add vault secret') }}</h2>
    <mat-dialog-content>
      @if (generated(); as value) {
        <p>Copy it now if you need it elsewhere: it is stored encrypted and never shown again.</p>
        <div class="generated">
          <code>{{ value }}</code>
          <button mat-icon-button (click)="copy(value)" matTooltip="Copy" aria-label="Copy secret"><mat-icon>content_copy</mat-icon></button>
        </div>
      } @else {
        <mat-form-field appearance="fill">
          <mat-label>Name</mat-label>
          <input matInput [(ngModel)]="name" [readonly]="!!overwrite" placeholder="postgres-password" />
        </mat-form-field>
        <mat-form-field appearance="fill">
          <mat-label>Value</mat-label>
          <input matInput type="password" autocomplete="new-password" [(ngModel)]="value" />
          <mat-hint>Leave empty to generate a strong random value</mat-hint>
        </mat-form-field>
        @if (error(); as err) { <p class="error">{{ err }}</p> }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (generated()) {
        <button mat-flat-button (click)="ref.close(name.trim())">Done</button>
      } @else {
        <button mat-button (click)="ref.close(null)" [disabled]="saving()">Cancel</button>
        <button mat-flat-button (click)="save()" [disabled]="saving() || !name.trim()">{{ saving() ? 'Saving…' : 'Save' }}</button>
      }
    </mat-dialog-actions>
  `,
})
export class SecretDialog {
  protected readonly ref = inject<MatDialogRef<SecretDialog, string | null>>(MatDialogRef);
  private readonly api = inject(SecretsService);
  private readonly toast = inject(NotificationService);
  /** A name to replace the value of; otherwise a new secret. */
  protected readonly overwrite = inject<string | null>(MAT_DIALOG_DATA, { optional: true });

  protected name = this.overwrite ?? '';
  protected value = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly generated = signal<string | null>(null);

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(this.api.apiSecretsPost({ name: this.name.trim(), value: this.value || null }));
      if (result?.value) this.generated.set(result.value);
      else this.ref.close(this.name.trim());
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }

  protected copy(text: string): void {
    void navigator.clipboard?.writeText(text).then(() => this.toast.success('Secret copied'));
  }
}

/** The encrypted secrets vault: names only; values are write-only. Projects map env vars to these. */
@Component({
  selector: 'app-vault-secrets',
  imports: [LocalDatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-list.scss',
  template: `
    <div class="toolbar">
      <p class="intro">Encrypted at rest. Map them to environment variables in a project's Settings tab.</p>
      <button matButton="filled" (click)="add(null)"><mat-icon>add</mat-icon> Add secret</button>
    </div>
    @if (loading() && secrets().length === 0) { <mat-progress-bar mode="indeterminate" /> }
    @if (secrets().length === 0 && !loading()) {
      <p class="x-empty">The vault is empty.</p>
    } @else {
      <div class="scroll">
        <div class="x-list">
          @for (s of secrets(); track s.id) {
            <div class="x-row">
              <span class="x-shape lg tertiary"><mat-icon>lock</mat-icon></span>
              <span class="main"><span class="name mono">{{ s.name }}</span></span>
              <span class="side">Added {{ s.createdAt | localDate }}</span>
              <span class="actions">
                <button mat-icon-button (click)="add(s.name)" matTooltip="Replace value" aria-label="Replace value"><mat-icon>edit</mat-icon></button>
                <button mat-icon-button class="danger" (click)="remove(s)" matTooltip="Delete" aria-label="Delete secret"><mat-icon>delete</mat-icon></button>
              </span>
            </div>
          }
        </div>
      </div>
    }
  `,
})
export class VaultSecrets {
  private readonly api = inject(SecretsService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(NotificationService);

  protected readonly secrets = signal<SecretDto[]>([]);
  protected readonly loading = signal(false);

  constructor() {
    this.reload();
  }

  private reload(): void {
    this.loading.set(true);
    this.api.apiSecretsGet().subscribe({
      next: (s) => this.secrets.set(s),
      error: (err) => this.toast.showError('Failed to load secrets', err),
    }).add(() => this.loading.set(false));
  }

  protected add(overwrite: string | null): void {
    this.dialog.open(SecretDialog, { data: overwrite, width: '520px', maxWidth: '95vw' })
      .afterClosed().subscribe((name) => name && this.reload());
  }

  protected async remove(s: SecretDto): Promise<void> {
    const ok = await this.confirm.ask({ title: 'Delete secret?', message: `"${s.name}" will be deleted from the vault. This cannot be undone.`, confirmText: 'Delete', destructive: true });
    if (!ok) return;
    this.api.apiSecretsNameDelete(s.name).subscribe({
      next: () => this.reload(),
      error: (err) => this.toast.showError('Could not delete the secret', err),
    });
  }
}
