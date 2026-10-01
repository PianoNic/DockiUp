import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { NotificationChannelDto, NotificationsService } from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { NotificationService, errorMessage } from '../shared/services/notification.service';
import { CHANNEL_TYPES, ChannelDialog, EVENTS } from './channel-dialog';

/** Notification channels: which service, which events, and a test button per channel. */
@Component({
  selector: 'app-notification-channels',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../activity/activity.scss',
  template: `
    <div class="activity-header">
      <p class="activity-subtitle">Discord, Slack, Telegram, ntfy or any webhook. Each channel picks the events it receives.</p>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon> Add channel</button>
    </div>
    @if (loading() && channels().length === 0) { <mat-progress-bar mode="indeterminate" /> }
    @if (channels().length === 0 && !loading()) {
      <p class="activity-empty">No channels yet. Add one to hear about failed deployments and nodes going offline.</p>
    } @else {
      <div class="table-scroll">
        <table class="activity-table">
          <thead><tr><th>Name</th><th>Type</th><th>Events</th><th>Enabled</th><th></th></tr></thead>
          <tbody>
            @for (c of channels(); track c.id) {
              <tr>
                <td class="target">{{ c.name }}</td>
                <td>{{ typeLabel(c.type) }}</td>
                <td>
                  @for (e of c.events; track e) { <span class="chip">{{ eventLabel(e) }}</span> } @empty { <span class="details">none</span> }
                </td>
                <td>
                  <mat-slide-toggle [checked]="c.enabled" (change)="setEnabled(c, $event.checked)" [attr.aria-label]="'Enable ' + c.name" />
                </td>
                <td>
                  <div class="row-actions">
                    <button mat-stroked-button (click)="test(c)" [disabled]="testing()[c.id]">
                      <mat-icon>send</mat-icon> {{ testing()[c.id] ? 'Sending…' : 'Send test' }}
                    </button>
                    <button mat-icon-button (click)="edit(c)" matTooltip="Edit" aria-label="Edit channel"><mat-icon>edit</mat-icon></button>
                    <button mat-icon-button (click)="remove(c)" matTooltip="Delete" aria-label="Delete channel"><mat-icon>delete</mat-icon></button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `.chip { margin: 2px 4px 2px 0; }`,
})
export class NotificationChannels {
  private readonly api = inject(NotificationsService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(NotificationService);

  protected readonly channels = signal<NotificationChannelDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly testing = signal<Record<string, boolean>>({});

  constructor() {
    this.reload();
  }

  protected typeLabel = (t: string) => CHANNEL_TYPES.find((x) => x.value === t)?.label ?? t;
  protected eventLabel = (e: string) => EVENTS.find((x) => x.value === e)?.label ?? e;

  private reload(): void {
    this.loading.set(true);
    this.api.listNotificationChannels().subscribe({
      next: (c) => this.channels.set(c),
      error: (err) => this.toast.showError('Failed to load notification channels', err),
    }).add(() => this.loading.set(false));
  }

  protected edit(channel: NotificationChannelDto | null): void {
    this.dialog.open(ChannelDialog, { data: channel, width: '640px', maxWidth: '95vw' })
      .afterClosed().subscribe((saved) => saved && this.reload());
  }

  protected async setEnabled(c: NotificationChannelDto, enabled: boolean): Promise<void> {
    try {
      await firstValueFrom(this.api.updateNotificationChannel(c.id, {
        name: c.name, type: c.type, enabled, events: c.events, url: c.url, target: c.target, headerName: c.headerName, secret: null,
      }));
    } catch (err) {
      this.toast.showError('Could not update the channel', err);
    }
    this.reload();
  }

  protected test(c: NotificationChannelDto): void {
    this.testing.update((t) => ({ ...t, [c.id]: true }));
    this.api.testNotificationChannel(c.id).subscribe({
      next: () => this.toast.success(`Test sent to ${c.name}`),
      error: (err) => this.toast.error(errorMessage(err)),
    }).add(() => this.testing.update((t) => ({ ...t, [c.id]: false })));
  }

  protected async remove(c: NotificationChannelDto): Promise<void> {
    const ok = await this.confirm.ask({ title: 'Delete channel?', message: `"${c.name}" will stop receiving notifications.`, confirmText: 'Delete', destructive: true });
    if (!ok) return;
    this.api.deleteNotificationChannel(c.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.toast.showError('Could not delete the channel', err),
    });
  }
}
