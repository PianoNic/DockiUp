import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ImageUpdateDto, ImageUpdatePolicy, ImageUpdateSettingsDto, ImageUpdatesService } from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { NotificationService } from '../shared/services/notification.service';
import { ImageUpdateStore } from '../shared/stores/image-update.store';
import { TagPickerData, TagPickerDialog } from './tag-picker-dialog';

interface ServiceRow {
  service: string;
  /** Last check's result; null until the service was checked. */
  check: ImageUpdateDto | null;
  pinned: string | null;
  excluded: boolean;
}

/** Image updates for one project: policy (#69), per-service status (#68) and tag pinning (#70). */
@Component({
  selector: 'app-project-images',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule, LocalDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['./project-deployments.scss', '../activity/activity.scss'],
  styles: `
    .images-panel { height: 100%; }
    .images-head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .images-head .section-title { margin: 0; }
    .images-head .spacer { flex: 1; }
    .policy { width: 280px; }
    .table-scroll { flex: 1; }
    .image-cell { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .note { color: var(--mat-sys-on-surface-variant); font-size: 12.5px; }
  `,
  template: `
    <section class="panel images-panel">
      <div class="images-head">
        <h2 class="section-title"><span class="x-shape tonal sm"><mat-icon>system_update</mat-icon></span> Images</h2>
        <mat-form-field appearance="fill" subscriptSizing="dynamic" class="policy">
          <mat-label>When a newer image is published</mat-label>
          <mat-select [value]="settings()?.policy" (selectionChange)="setPolicy($event.value)" [disabled]="!settings()">
            <mat-option value="Off">Ignore (don't check)</mat-option>
            <mat-option value="Notify">Notify only</mat-option>
            <mat-option value="Auto">Update automatically</mat-option>
          </mat-select>
        </mat-form-field>
        <span class="spacer"></span>
        <button matButton="tonal" (click)="store.checkNow(projectId())" [disabled]="store.checking() || settings()?.policy === 'Off'">
          <mat-icon>update</mat-icon>
          {{ store.checking() ? 'Checking…' : 'Check now' }}
        </button>
      </div>
      <p class="hint">
        Running images are compared with their registry (same tag) every few hours.
        @switch (settings()?.policy) {
          @case ('Auto') { Newer images are pulled and only the changed services are recreated, as an "ImageUpdate" deployment. }
          @case ('Off') { Checks are off for this project. }
          @default { Newer images are only flagged; deploy to apply them. }
        }
      </p>

      @if (rows().length === 0) {
        <p class="empty">No services yet: deploy the project first.</p>
      } @else {
        <div class="table-scroll">
          <table class="activity-table">
            <thead>
              <tr><th>Service</th><th>Image</th><th>Status</th><th>Checked</th><th></th></tr>
            </thead>
            <tbody>
              @for (r of rows(); track r.service) {
                <tr>
                  <td class="target">{{ r.service }}</td>
                  <td>
                    <div class="image-cell">
                      <span class="mono">{{ r.pinned ?? r.check?.image ?? '—' }}</span>
                      @if (r.pinned) {
                        <span class="x-chip" matTooltip="Chosen here (dockiup.override.yml); reset to use the compose file's image again">
                          <mat-icon>push_pin</mat-icon>pinned
                        </span>
                      }
                    </div>
                  </td>
                  <td>
                    @if (r.excluded) {
                      <span class="state-badge" data-state="stopped">Excluded</span>
                    } @else if (r.check?.updateAvailable) {
                      <span class="state-badge" data-state="update" [matTooltip]="'Registry: ' + r.check?.latestDigest">Update available</span>
                    } @else if (r.check?.note) {
                      <span class="note">{{ r.check?.note }}</span>
                    } @else if (r.check) {
                      <span class="state-badge" data-state="running" [matTooltip]="r.check.currentDigest ?? ''">Up to date</span>
                    } @else {
                      <span class="note">Not checked yet</span>
                    }
                  </td>
                  <td class="when">{{ r.check ? (r.check.checkedAt | localDate: 'short') : '' }}</td>
                  <td>
                    <div class="row-actions">
                      <button mat-icon-button (click)="chooseTag(r)" matTooltip="Choose a tag to deploy" aria-label="Choose a tag to deploy">
                        <mat-icon>sell</mat-icon>
                      </button>
                      @if (r.pinned) {
                        <button mat-icon-button (click)="reset(r)" matTooltip="Reset to file default" aria-label="Reset to file default">
                          <mat-icon>undo</mat-icon>
                        </button>
                      }
                      <button mat-icon-button (click)="toggleExcluded(r)" [disabled]="!settings()"
                        [matTooltip]="r.excluded ? 'Include in update checks' : 'Exclude from update checks'"
                        [attr.aria-label]="r.excluded ? 'Include in update checks' : 'Exclude from update checks'">
                        <mat-icon>{{ r.excluded ? 'notifications_off' : 'notifications_active' }}</mat-icon>
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
})
export class ProjectImages {
  readonly projectId = input.required<string>();
  /** Compose services currently running (so unchecked services are listed too). */
  readonly services = input<string[]>([]);
  /** A deployment was queued (tag picked or reset): the page shows it running. */
  readonly deployed = output<void>();

  protected readonly store = inject(ImageUpdateStore);
  private readonly api = inject(ImageUpdatesService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);

  protected readonly settings = signal<ImageUpdateSettingsDto | null>(null);

  protected readonly rows = computed<ServiceRow[]>(() => {
    const id = this.projectId();
    const checks = this.store.all().filter((u) => u.projectId === id);
    const settings = this.settings();
    const names = [...new Set([...this.services(), ...checks.map((c) => c.serviceName), ...Object.keys(settings?.pinnedImages ?? {})])].sort();
    return names.map((service) => ({
      service,
      check: checks.find((c) => c.serviceName === service) ?? null,
      pinned: settings?.pinnedImages[service] ?? null,
      excluded: settings?.excludedServices.includes(service) ?? false,
    }));
  });

  constructor() {
    void this.store.ensureLoaded();
    effect(() => void this.loadSettings(this.projectId()));
  }

  private async loadSettings(id: string): Promise<void> {
    try {
      this.settings.set(await firstValueFrom(this.api.getImageUpdateSettings(id)));
    } catch (err) {
      this.notifications.showError('Failed to load image settings', err);
    }
  }

  private async save(policy: ImageUpdatePolicy, excluded: string[]): Promise<void> {
    try {
      await firstValueFrom(this.api.setImageUpdateSettings(this.projectId(), { policy, excludedServices: excluded }));
      await this.loadSettings(this.projectId());
      await this.store.load();
    } catch (err) {
      this.notifications.showError('Failed to save image settings', err);
    }
  }

  protected async setPolicy(policy: ImageUpdatePolicy): Promise<void> {
    await this.save(policy, this.settings()?.excludedServices ?? []);
    this.notifications.success(
      { Off: 'Image update checks turned off', Notify: 'Image updates will be flagged', Auto: 'Image updates will be deployed automatically' }[policy]);
  }

  protected async toggleExcluded(r: ServiceRow): Promise<void> {
    const s = this.settings();
    if (!s) return;
    const excluded = r.excluded ? s.excludedServices.filter((x) => x !== r.service) : [...s.excludedServices, r.service];
    await this.save(s.policy, excluded);
  }

  protected async chooseTag(r: ServiceRow): Promise<void> {
    const image = r.pinned ?? r.check?.image ?? null;
    const ref = this.dialog.open<TagPickerDialog, TagPickerData, string | null>(TagPickerDialog, {
      data: { projectId: this.projectId(), serviceName: r.service, image },
      width: '520px',
      maxWidth: '95vw',
    });
    const tag = await firstValueFrom(ref.afterClosed());
    if (!tag) return;
    try {
      await firstValueFrom(this.api.pinServiceImage(this.projectId(), r.service, { tag }));
      this.notifications.success(`${r.service} pinned to ${tag}; deployment queued`);
      await this.loadSettings(this.projectId());
      this.deployed.emit();
    } catch (err) {
      this.notifications.showError(`Failed to deploy ${tag}`, err);
    }
  }

  protected async reset(r: ServiceRow): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Reset ${r.service}?`,
      message: `Removes the pinned ${r.pinned} and redeploys with the image from the compose file.`,
      confirmText: 'Reset and deploy',
    });
    if (!ok) return;
    try {
      await firstValueFrom(this.api.resetServiceImage(this.projectId(), r.service));
      this.notifications.success(`${r.service} reset; deployment queued`);
      await this.loadSettings(this.projectId());
      this.deployed.emit();
    } catch (err) {
      this.notifications.showError(`Failed to reset ${r.service}`, err);
    }
  }
}
