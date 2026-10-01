import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ActivityEntryDto, ActivityService } from '../api';

/** Audit log of everything DockiUp did — deploys, project lifecycle, node changes (KRINT-style). */
@Component({
  selector: 'app-activity',
  imports: [LocalDatePipe, MatIconModule, MatButtonModule, MatProgressBarModule, MatTooltipModule],
  templateUrl: './activity.html',
  styleUrls: ['./activity.scss', './activity-page.scss'],
})
export class Activity implements OnInit {
  private activityService = inject(ActivityService);

  readonly entries = signal<ActivityEntryDto[]>([]);
  readonly loading = signal(false);
  readonly filter = signal('');
  readonly kind = signal<Kind>('all');
  readonly kinds: { key: Kind; label: string }[] = [
    { key: 'all', label: 'Everything' },
    { key: 'deploy', label: 'Deploys' },
    { key: 'lifecycle', label: 'Lifecycle' },
    { key: 'files', label: 'Files & secrets' },
    { key: 'hosts', label: 'Hosts' },
  ];

  readonly filtered = computed(() => {
    const q = this.filter().toLowerCase().trim();
    const kind = this.kind();
    return this.entries().filter(e =>
      (kind === 'all' || kindOf(e.action) === kind) &&
      (!q || `${e.action} ${e.target} ${e.details ?? ''} ${e.actorName ?? ''}`.toLowerCase().includes(q)));
  });

  async ngOnInit() {
    await this.reload();
  }

  async reload() {
    this.loading.set(true);
    try {
      const rows = await firstValueFrom(this.activityService.apiActivityGet(200 as any));
      this.entries.set(rows ?? []);
    } catch {
      this.entries.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  setFilter(value: string) {
    this.filter.set(value);
  }

  /** Shape colour per kind: deploys tonal, removals and failures red, cleanups tertiary, the rest neutral. */
  shapeClass(action: string): string {
    if (action.includes('failed') || action.endsWith('.remove') || action.endsWith('.delete') || action === 'remove') return 'err';
    if (action.startsWith('deploy')) return 'tonal round';
    if (action.startsWith('cleanup') || action === 'prune' || action.endsWith('.remove-resource')) return 'tertiary';
    return '';
  }

  activityIcon(action: string): string {
    if (action.startsWith('deploy')) return 'rocket_launch';
    if (action.startsWith('node.create')) return 'dns';
    if (action.startsWith('node.delete')) return 'delete';
    if (action.startsWith('cleanup') || action === 'prune') return 'cleaning_services';
    if (action.endsWith('.remove')) return 'delete';
    if (action.includes('stop')) return 'stop';
    if (action.includes('restart')) return 'refresh';
    if (action.includes('update')) return 'system_update';
    return 'bolt';
  }
}

type Kind = 'all' | 'deploy' | 'lifecycle' | 'files' | 'hosts';

function kindOf(action: string): Kind {
  if (action.startsWith('deploy')) return 'deploy';
  if (/^(node|cleanup|prune|image\.remove|volume|network)/.test(action)) return 'hosts';
  if (/^(file|secrets|image|git)/.test(action)) return 'files';
  return 'lifecycle';
}
