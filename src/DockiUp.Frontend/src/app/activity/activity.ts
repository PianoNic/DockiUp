import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { ActivityEntryDto, ActivityService } from '../api';

/** Audit log of everything DockiUp did — deploys, project lifecycle, node changes (KRINT-style). */
@Component({
  selector: 'app-activity',
  imports: [DatePipe, MatIconModule, MatButtonModule, MatProgressBarModule],
  templateUrl: './activity.html',
  styleUrl: './activity.scss',
})
export class Activity implements OnInit {
  private activityService = inject(ActivityService);

  readonly entries = signal<ActivityEntryDto[]>([]);
  readonly loading = signal(false);
  readonly filter = signal('');

  readonly filtered = computed(() => {
    const q = this.filter().toLowerCase().trim();
    if (!q) return this.entries();
    return this.entries().filter(e =>
      `${e.action} ${e.target} ${e.details ?? ''} ${e.actorName ?? ''}`.toLowerCase().includes(q));
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

  activityIcon(action: string): string {
    if (action.startsWith('deploy')) return 'rocket_launch';
    if (action.startsWith('node.create')) return 'dns';
    if (action.startsWith('node.delete')) return 'delete';
    if (action.includes('stop')) return 'stop';
    if (action.includes('restart')) return 'refresh';
    if (action.includes('update')) return 'system_update';
    return 'bolt';
  }
}
