import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { ImageUpdatesService } from '../api';
import { errorMessage } from '../shared/services/notification.service';

export interface TagPickerData {
  projectId: string;
  serviceName: string;
  /** Image the service runs now (`repo:tag`), when known from a check or a pin. */
  image: string | null;
}

/** The tag of an image reference (`latest` when none is written). */
export function imageTag(image: string): string {
  const ref = image.split('@')[0];
  const colon = ref.lastIndexOf(':');
  return colon > ref.lastIndexOf('/') ? ref.slice(colon + 1) : 'latest';
}

const SHOWN = 300;

/** Lists the registry's tags for a service's image (newest first) and returns the one picked. */
@Component({
  selector: 'app-tag-picker-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .filter { width: 100%; }
    .tags {
      height: 320px;
      overflow: auto;
      display: flex;
      flex-direction: column;
      gap: 2px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 10px;
      padding: 4px;
    }
    .tag {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      border: none;
      border-radius: 8px;
      background: transparent;
      color: inherit;
      font: 14px ui-monospace, SFMono-Regular, Menlo, monospace;
      text-align: left;
      cursor: pointer;
      &:hover { background: var(--mat-sys-surface-container-high); }
      &.selected { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
      .current { margin-left: auto; font: 12px var(--mat-sys-body-small-font, inherit); color: var(--mat-sys-on-surface-variant); }
    }
    .hint, .error { margin: 8px 0 0; font-size: 13px; color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); }
  `,
  template: `
    <h2 mat-dialog-title>Choose a tag for {{ data.serviceName }}</h2>
    <mat-dialog-content>
      <p class="hint">@if (data.image) { Runs <code>{{ data.image }}</code> now. } The chosen tag is written to <code>dockiup.override.yml</code>; the compose file stays untouched.</p>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="filter">
        <mat-label>Filter tags</mat-label>
        <mat-icon matPrefix>search</mat-icon>
        <input matInput [value]="filter()" (input)="filter.set($any($event.target).value)" cdkFocusInitial />
      </mat-form-field>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      } @else {
        <div class="tags" role="listbox" aria-label="Tags">
          @for (tag of shown(); track tag) {
            <button type="button" class="tag" role="option" [class.selected]="tag === selected()" [attr.aria-selected]="tag === selected()"
              (click)="selected.set(tag)">
              {{ tag }}
              @if (tag === current) { <span class="current">current</span> }
            </button>
          } @empty {
            <p class="hint">No tags match "{{ filter() }}".</p>
          }
        </div>
        @if (matches().length > shown().length) {
          <p class="hint">Showing {{ shown().length }} of {{ matches().length }} tags; filter to narrow down.</p>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">Cancel</button>
      <button mat-flat-button [mat-dialog-close]="selected()" [disabled]="!selected() || selected() === current">
        Deploy {{ selected() ?? '' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class TagPickerDialog {
  protected readonly data = inject<TagPickerData>(MAT_DIALOG_DATA);
  private readonly api = inject(ImageUpdatesService);

  protected readonly current = this.data.image ? imageTag(this.data.image) : null;
  protected readonly tags = signal<string[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal('');
  protected readonly selected = signal<string | null>(null);

  protected readonly matches = computed(() => {
    const q = this.filter().trim().toLowerCase();
    return q ? this.tags().filter((t) => t.toLowerCase().includes(q)) : this.tags();
  });
  protected readonly shown = computed(() => this.matches().slice(0, SHOWN));

  constructor() {
    firstValueFrom(this.api.listServiceTags(this.data.projectId, this.data.serviceName))
      .then((tags) => this.tags.set(tags))
      .catch((err) => this.error.set(`Could not list tags: ${errorMessage(err)}`))
      .finally(() => this.loading.set(false));
  }
}
