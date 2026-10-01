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
  styleUrl: './settings-list.scss',
  template: `
    <div class="toolbar">
      <p class="intro">Discord, Slack, Telegram, ntfy or any webhook. Each channel picks the events it receives.</p>
      <button matButton="filled" (click)="edit(null)"><mat-icon>add</mat-icon> Add channel</button>
    </div>
    @if (loading() && channels().length === 0) { <mat-progress-bar mode="indeterminate" /> }
    @if (channels().length === 0 && !loading()) {
      <p class="x-empty">No channels yet. Add one to hear about failed deployments and nodes going offline.</p>
    } @else {
      <div class="scroll">
        <div class="x-list">
          @for (c of channels(); track c.id) {
            <div class="x-row">
              <span [class]="typeShape(c.type)" [class.off]="!c.enabled"><mat-icon>{{ typeIcon(c.type) }}</mat-icon></span>
              <span class="main" [class.off]="!c.enabled">
                <span class="name">{{ c.name }}</span>
                <span class="meta">{{ typeLabel(c.type) }}@if (c.target || c.url) { · {{ c.target || c.url }} }</span>
              </span>
              <span class="chips">
                @for (e of c.events; track e) { <span class="x-chip">{{ eventLabel(e) }}</span> } @empty { <span class="meta">No events</span> }
              </span>
              <mat-slide-toggle [checked]="c.enabled" (change)="setEnabled(c, $event.checked)" [attr.aria-label]="'Enable ' + c.name" />
              <span class="actions">
                <button mat-icon-button (click)="test(c)" [disabled]="testing()[c.id]"
                  [matTooltip]="testing()[c.id] ? 'Sending…' : 'Send test'" aria-label="Send test">
                  <mat-icon>{{ testing()[c.id] ? 'hourglass_top' : 'send' }}</mat-icon>
                </button>
                <button mat-icon-button (click)="edit(c)" matTooltip="Edit" aria-label="Edit channel"><mat-icon>edit</mat-icon></button>
                <button mat-icon-button class="danger" (click)="remove(c)" matTooltip="Delete" aria-label="Delete channel"><mat-icon>delete</mat-icon></button>
              </span>
            </div>
          }
        </div>
      </div>
    }
  `,
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
  protected typeIcon = (t: string) =>
    ({ Discord: 'forum', Slack: 'tag', Telegram: 'send', Ntfy: 'phone_iphone', Webhook: 'webhook' } as Record<string, string>)[t] ?? 'notifications';
  // Each service gets its own container colour, so the list is easy to scan.
  protected typeShape = (t: string) =>
    'x-shape lg ' + (({ Discord: 'tonal', Telegram: 'tonal', Slack: 'tertiary', Ntfy: 'tertiary' } as Record<string, string>)[t] ?? '');

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
