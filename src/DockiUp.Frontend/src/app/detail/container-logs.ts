import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, model, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ContainerDto, ContainerService } from '../api';
import { NotificationService, errorMessage } from '../shared/services/notification.service';

/** Splits a search into terms: whitespace-separated words, "quoted phrases" kept whole. Lower-cased. */
export function searchTerms(query: string): string[] {
  return [...query.matchAll(/"([^"]+)"|(\S+)/g)].map((m) => (m[1] ?? m[2]).toLowerCase());
}

/** Lines containing any term; with invert, the lines containing none. No terms = every line. */
export function filterLines(lines: string[], terms: string[], invert: boolean): string[] {
  if (!terms.length) return lines;
  return lines.filter((line) => {
    const l = line.toLowerCase();
    return terms.some((t) => l.includes(t)) !== invert;
  });
}

// Docker's RFC3339Nano trims trailing zeros; pad the fraction so timestamps sort as strings.
const sortKey = (line: string) => line.slice(0, line.indexOf(' ')).replace(/\.(\d+)Z$/, (_, f: string) => `.${f.padEnd(9, '0')}Z`);

/** The project page's log view: one or all containers, stream/tail/timestamp options, search with exclude, download. */
@Component({
  selector: 'app-container-logs',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
  ],
  templateUrl: './container-logs.html',
  styleUrl: './container-logs.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContainerLogs {
  private readonly containerService = inject(ContainerService);
  private readonly notifications = inject(NotificationService);

  readonly containers = input.required<ContainerDto[]>();
  readonly nodeId = input<string | null | undefined>(null);
  readonly projectName = input('project');
  /** Empty string = all containers, otherwise one container id. */
  readonly selected = model('');

  protected readonly tailOptions = [100, 200, 500, 1000, 5000, 0];
  protected readonly tail = signal(200);
  protected readonly streams = signal<string[]>(['stdout', 'stderr']);

  protected toggleStream(s: string): void {
    this.streams.update((list) => (list.includes(s) ? list.filter((x) => x !== s) : [...list, s]));
  }
  protected readonly timestamps = signal(false);
  protected readonly search = signal('');
  protected readonly invert = signal(false);

  protected readonly loading = signal(false);
  protected readonly lines = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);

  protected readonly visible = computed(() => filterLines(this.lines(), searchTerms(this.search()), this.invert()));

  private readonly output = viewChild<ElementRef<HTMLDivElement>>('output');

  // The project list is re-sent on every state change; only a different set of containers should refetch.
  private readonly containerKey = computed(() => this.containers().map((c) => c.id).join(','));

  constructor() {
    effect(() => {
      this.containerKey();
      this.selected();
      this.tail();
      this.streams();
      this.timestamps();
      // load() reads the full container list; untracked so only the inputs above trigger a refetch.
      untracked(() => void this.load());
    });
    effect(() => {
      this.visible();
      setTimeout(() => {
        const el = this.output()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });
  }

  async load(): Promise<void> {
    const containers = this.containers();
    const id = this.selected();
    const targets = id ? containers.filter((c) => c.id === id) : containers;
    if (!targets.length) {
      this.lines.set([]);
      this.message.set(id ? 'Container not found.' : 'No containers in this project.');
      return;
    }
    const [stdout, stderr] = [this.streams().includes('stdout'), this.streams().includes('stderr')];
    const timestamps = this.timestamps();
    this.loading.set(true);
    try {
      const results = await Promise.all(
        targets.map(async (c) => {
          try {
            const text = await firstValueFrom(
              this.containerService.getContainerLogs(c.id, this.tail() as never, this.nodeId() ?? undefined, stdout, stderr, timestamps),
            );
            return { name: c.name, lines: (text ?? '').split(/\r?\n/).filter((l) => l.length > 0) };
          } catch (err) {
            this.notifications.showError(`Failed to load logs for ${c.name}`, err);
            return { name: c.name, lines: [`(failed: ${errorMessage(err)})`] };
          }
        }),
      );
      let merged: string[];
      if (targets.length === 1) {
        merged = results[0].lines;
      } else if (timestamps) {
        // Interleave by time, keeping the timestamp first and naming the container after it.
        merged = results
          .flatMap((r) => r.lines.map((l) => { const i = l.indexOf(' '); return `${l.slice(0, i)} [${r.name}]${l.slice(i)}`; }))
          .sort((a, b) => (sortKey(a) < sortKey(b) ? -1 : sortKey(a) > sortKey(b) ? 1 : 0));
      } else {
        merged = results.flatMap((r) => r.lines.map((l) => `[${r.name}] ${l}`));
      }
      this.lines.set(merged);
      this.message.set(merged.length ? null : '(no output)');
    } finally {
      this.loading.set(false);
    }
  }

  protected download(): void {
    const name = this.selected() ? (this.containers().find((c) => c.id === this.selected())?.name ?? 'container') : this.projectName();
    const blob = new Blob([this.visible().join('\n') + '\n'], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${name}-${new Date().toISOString().replace(/[:.]/g, '-')}.log`;
    a.click();
    URL.revokeObjectURL(url);
  }
}
