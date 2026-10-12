import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { ImageUpdateStore, updatesTooltip } from '../../stores/image-update.store';
import { projectLink } from '../../project-url';

/** "Update available" badge for a project (or one of its services), or with `compact` an icon whose tooltip says it;
 *  renders nothing when up to date. */
@Component({
  selector: 'app-image-update-badge',
  imports: [MatIconModule, MatTooltipModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .update-icon { display: inline-flex; vertical-align: middle; color: #fb8c00; text-decoration: none; }
    .update-icon mat-icon { font-size: 20px; width: 20px; height: 20px; }
  `,
  template: `
    @if (updates().length && compact()) {
      @if (dockerProjectName(); as name) {
        <a class="update-icon" [routerLink]="link(name)" [queryParams]="{ tab: 'images' }" (click)="$event.stopPropagation()"
          [matTooltip]="'Update available. ' + tooltip()" [attr.aria-label]="'Update available. ' + tooltip()"><mat-icon>arrow_circle_up</mat-icon></a>
      } @else {
        <span class="update-icon" [matTooltip]="'Update available. ' + tooltip()" [attr.aria-label]="'Update available. ' + tooltip()"><mat-icon>arrow_circle_up</mat-icon></span>
      }
    } @else if (updates().length) {
      @if (dockerProjectName(); as name) {
        <a class="state-badge badge-link" data-state="update" [routerLink]="link(name)" [queryParams]="{ tab: 'images' }"
          (click)="$event.stopPropagation()" [matTooltip]="tooltip()" matTooltipShowDelay="800" [attr.aria-label]="tooltip()">Update available</a>
      } @else {
        <span class="state-badge" data-state="update" [matTooltip]="tooltip()" matTooltipShowDelay="800" [attr.aria-label]="tooltip()">Update available</span>
      }
    }
  `,
})
export class ImageUpdateBadge {
  private readonly store = inject(ImageUpdateStore);

  readonly projectId = input<string | null | undefined>();
  /** The project's docker name: makes the badge a link to its Images tab. */
  readonly dockerProjectName = input<string | null | undefined>();
  readonly nodeId = input<string | null | undefined>();
  /** Limit to one service (containers table rows). */
  readonly service = input<string | null>(null);
  /** An icon instead of the text badge (rows that repeat it). */
  readonly compact = input(false, { transform: booleanAttribute });

  protected link(name: string): string[] {
    return projectLink(name, this.nodeId());
  }

  protected readonly updates = computed(() => {
    const all = this.store.updatesFor(this.projectId());
    const service = this.service();
    return service ? all.filter((u) => u.serviceName === service) : all;
  });

  protected readonly tooltip = computed(() => updatesTooltip(this.updates()));

  constructor() {
    void this.store.ensureLoaded();
  }
}
