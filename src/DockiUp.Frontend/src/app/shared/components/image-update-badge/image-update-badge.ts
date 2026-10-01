import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ImageUpdateStore, updatesTooltip } from '../../stores/image-update.store';

/** "Update available" badge for a project (or one of its services); renders nothing when up to date. */
@Component({
  selector: 'app-image-update-badge',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (updates().length) {
      <span class="state-badge" data-state="update" [matTooltip]="tooltip()" [attr.aria-label]="tooltip()">Update available</span>
    }
  `,
})
export class ImageUpdateBadge {
  private readonly store = inject(ImageUpdateStore);

  readonly projectId = input<string | null | undefined>();
  /** Limit to one service (containers table rows). */
  readonly service = input<string | null>(null);

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
