import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { SettingDto, SettingsService } from '../api';
import { NotificationService } from '../shared/services/notification.service';

/** DockiUp's own settings. One set as an environment variable is locked here and shows which one. */
@Component({
  selector: 'app-general-settings',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-list.scss',
  styles: `
    .group { margin: 8px 0 4px 4px; font-size: 13px; font-weight: 700; letter-spacing: .04em; text-transform: uppercase; color: var(--mat-sys-primary); }
    .x-row { align-items: center; flex-wrap: wrap; }
    .main .meta { white-space: normal; }
    mat-form-field { width: 320px; max-width: 100%; }
    .lock { color: var(--mat-sys-on-surface-variant); }
  `,
  template: `
    <div class="toolbar">
      <p class="intro">Applies right away. A setting given as an environment variable is locked here; change it in compose.yml.</p>
    </div>
    @if (loading() && settings().length === 0) { <mat-progress-bar mode="indeterminate" /> }
    <div class="scroll">
      @for (g of groups(); track g.name) {
        <p class="group">{{ g.name }}</p>
        <div class="x-list">
          @for (s of g.settings; track s.key) {
            <div class="x-row">
              <span class="main">
                <span class="name">{{ s.label }}</span>
                <span class="meta">{{ s.hint }}</span>
              </span>
              <mat-form-field appearance="fill" subscriptSizing="dynamic">
                <input matInput [type]="s.type === 'number' ? 'number' : s.type === 'url' ? 'url' : 'text'" [min]="s.min"
                  [disabled]="!!s.lockedBy || saving() === s.key"
                  [placeholder]="s.default ? 'Default: ' + s.default : 'Not set'"
                  [ngModel]="draft()[s.key] ?? s.value ?? ''" (ngModelChange)="edit(s.key, $event)"
                  (keydown.enter)="save(s)" />
                @if (s.lockedBy) { <mat-icon matSuffix class="lock" [matTooltip]="'Set by ' + s.lockedBy">lock</mat-icon> }
                @if (s.lockedBy) { <mat-hint>Set by <code>{{ s.lockedBy }}</code></mat-hint> }
              </mat-form-field>
              <span class="actions">
                @if (!s.lockedBy && dirty(s)) {
                  <button matButton="filled" (click)="save(s)" [disabled]="saving() === s.key">Save</button>
                }
              </span>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class GeneralSettings {
  private readonly api = inject(SettingsService);
  private readonly toast = inject(NotificationService);

  protected readonly settings = signal<SettingDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal<string | null>(null);
  protected readonly draft = signal<Record<string, string | undefined>>({});

  protected readonly groups = computed(() => {
    const groups = new Map<string, SettingDto[]>();
    for (const s of this.settings()) groups.set(s.group, [...(groups.get(s.group) ?? []), s]);
    return [...groups].map(([name, settings]) => ({ name, settings }));
  });

  constructor() {
    this.loading.set(true);
    this.api.listSettings().subscribe({
      next: (s) => this.settings.set(s),
      error: (err) => this.toast.showError('Failed to load settings', err),
    }).add(() => this.loading.set(false));
  }

  protected edit(key: string, value: unknown): void {
    this.draft.update((d) => ({ ...d, [key]: value == null ? '' : String(value) }));
  }

  protected dirty(s: SettingDto): boolean {
    const d = this.draft()[s.key];
    return d !== undefined && d !== (s.value ?? '');
  }

  protected async save(s: SettingDto): Promise<void> {
    if (s.lockedBy || !this.dirty(s)) return;
    this.saving.set(s.key);
    try {
      const saved = await firstValueFrom(this.api.saveSetting({ key: s.key, value: this.draft()[s.key] || null }));
      this.settings.update((all) => all.map((x) => (x.key === saved.key ? saved : x)));
      this.draft.update(({ [s.key]: _, ...rest }) => rest);
      this.toast.success(`${s.label} saved`);
    } catch (err) {
      this.toast.showError(`Could not save ${s.label.toLowerCase()}`, err);
    } finally {
      this.saving.set(null);
    }
  }
}
