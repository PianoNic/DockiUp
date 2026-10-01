import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { firstValueFrom } from 'rxjs';
import { NotificationChannelDto, NotificationChannelType, NotificationsService, SaveNotificationChannelRequest } from '../api';

/** Mirrors DockiUp.Domain.NotificationEvent (the API sends the names as strings; the generator inlines them). */
export type NotificationEvent = 'DeploymentSucceeded' | 'DeploymentFailed' | 'NodeOffline' | 'NodeOnline' | 'ImageUpdateAvailable' | 'CleanupReport';
import { errorMessage } from '../shared/services/notification.service';

export const CHANNEL_TYPES: { value: NotificationChannelType; label: string }[] = [
  { value: 'Discord', label: 'Discord' },
  { value: 'Slack', label: 'Slack' },
  { value: 'Telegram', label: 'Telegram' },
  { value: 'Ntfy', label: 'ntfy' },
  { value: 'Webhook', label: 'Webhook' },
];

export const EVENTS: { value: NotificationEvent; label: string }[] = [
  { value: 'DeploymentFailed', label: 'Deployment failed' },
  { value: 'DeploymentSucceeded', label: 'Deployment succeeded' },
  { value: 'NodeOffline', label: 'Node offline' },
  { value: 'NodeOnline', label: 'Node online' },
  { value: 'ImageUpdateAvailable', label: 'Image update available' },
  { value: 'CleanupReport', label: 'Cleanup report' },
];

/** Add or edit a notification channel. Stored credentials are never shown; leaving the field empty keeps them. */
@Component({
  selector: 'app-channel-dialog',
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    mat-form-field { width: 100%; }
    .row { display: flex; gap: 12px; }
    .events { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); margin: 4px 0 12px; }
    .label { font-weight: 500; margin: 8px 0 0; }
    .error { color: var(--mat-sys-error); font-size: 14px; }
  `,
  template: `
    <h2 mat-dialog-title>{{ existing ? 'Edit channel' : 'Add notification channel' }}</h2>
    <mat-dialog-content>
      <div class="row">
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput [(ngModel)]="name" placeholder="Ops alerts" required />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Type</mat-label>
          <mat-select [ngModel]="type()" (ngModelChange)="type.set($event)">
            @for (t of types; track t.value) { <mat-option [value]="t.value">{{ t.label }}</mat-option> }
          </mat-select>
        </mat-form-field>
      </div>

      @switch (type()) {
        @case ('Telegram') {
          <mat-form-field appearance="outline">
            <mat-label>Chat id</mat-label>
            <input matInput [(ngModel)]="target" placeholder="-1001234567890" />
            <mat-hint>The chat, group or channel the bot posts to</mat-hint>
          </mat-form-field>
        }
        @case ('Ntfy') {
          <div class="row">
            <mat-form-field appearance="outline">
              <mat-label>Server</mat-label>
              <input matInput [(ngModel)]="url" placeholder="https://ntfy.sh" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Topic</mat-label>
              <input matInput [(ngModel)]="target" placeholder="dockiup" />
            </mat-form-field>
          </div>
        }
        @case ('Webhook') {
          <mat-form-field appearance="outline">
            <mat-label>URL</mat-label>
            <input matInput [(ngModel)]="url" placeholder="https://example.com/hooks/dockiup" />
            <mat-hint>Receives a JSON POST: event, title, message, timestamp, data</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Secret header name</mat-label>
            <input matInput [(ngModel)]="headerName" placeholder="X-Webhook-Secret" />
          </mat-form-field>
        }
      }

      <mat-form-field appearance="outline">
        <mat-label>{{ secretLabel() }}</mat-label>
        <input matInput type="password" autocomplete="new-password" [(ngModel)]="secret"
          [placeholder]="existing?.secretMasked ? 'Leave empty to keep ' + existing!.secretMasked : ''" />
        @if (canClear()) {
          <button mat-button matSuffix type="button" (click)="clearSecret.set(!clearSecret())">{{ clearSecret() ? 'Keep' : 'Remove' }}</button>
        }
        <mat-hint>{{ canClear() && clearSecret() ? 'The stored value will be removed' : 'Stored encrypted; never shown again' }}</mat-hint>
      </mat-form-field>

      <p class="label">Send these events</p>
      <div class="events">
        @for (e of events; track e.value) {
          <mat-checkbox [checked]="selected().has(e.value)" (change)="toggle(e.value, $event.checked)">{{ e.label }}</mat-checkbox>
        }
      </div>
      <mat-slide-toggle [(ngModel)]="enabled">Enabled</mat-slide-toggle>

      @if (error(); as err) { <p class="error">{{ err }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)" [disabled]="saving()">Cancel</button>
      <button mat-flat-button (click)="save()" [disabled]="saving() || !name.trim()">{{ saving() ? 'Saving…' : 'Save' }}</button>
    </mat-dialog-actions>
  `,
})
export class ChannelDialog {
  protected readonly ref = inject<MatDialogRef<ChannelDialog, NotificationChannelDto | null>>(MatDialogRef);
  private readonly api = inject(NotificationsService);
  protected readonly existing = inject<NotificationChannelDto | null>(MAT_DIALOG_DATA);

  protected readonly types = CHANNEL_TYPES;
  protected readonly events = EVENTS;

  protected name = this.existing?.name ?? '';
  protected readonly type = signal<NotificationChannelType>(this.existing?.type ?? 'Discord');
  protected url = this.existing?.url ?? '';
  protected target = this.existing?.target ?? '';
  protected headerName = this.existing?.headerName ?? '';
  protected secret = '';
  protected enabled = this.existing?.enabled ?? true;
  protected readonly clearSecret = signal(false);
  protected readonly selected = signal(new Set<NotificationEvent>((this.existing?.events as NotificationEvent[] | undefined) ?? ['DeploymentFailed', 'NodeOffline']));
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  // Only the optional credentials (ntfy token, webhook header) can be removed; the others are required.
  protected readonly canClear = computed(() => !!this.existing?.secretMasked && (this.type() === 'Ntfy' || this.type() === 'Webhook'));
  protected readonly secretLabel = computed(() => {
    switch (this.type()) {
      case 'Telegram': return 'Bot token';
      case 'Ntfy': return 'Access token (optional)';
      case 'Webhook': return 'Secret header value (optional)';
      default: return 'Incoming webhook URL';
    }
  });

  protected toggle(event: NotificationEvent, on: boolean): void {
    this.selected.update((s) => {
      const next = new Set(s);
      if (on) next.add(event); else next.delete(event);
      return next;
    });
  }

  protected async save(): Promise<void> {
    const request: SaveNotificationChannelRequest = {
      name: this.name.trim(),
      type: this.type(),
      enabled: this.enabled,
      events: [...this.selected()],
      url: this.url.trim() || null,
      target: this.target.trim() || null,
      headerName: this.headerName.trim() || null,
      // null keeps the stored credential, '' removes it.
      secret: this.secret ? this.secret : this.clearSecret() ? '' : null,
    };
    this.saving.set(true);
    this.error.set(null);
    try {
      const saved = await firstValueFrom(this.existing
        ? this.api.updateNotificationChannel(this.existing.id, request)
        : this.api.createNotificationChannel(request));
      this.ref.close(saved);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }
}
