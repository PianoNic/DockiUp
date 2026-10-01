import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface ChartPoint {
  /** Epoch milliseconds. */
  t: number;
  v: number;
}

const W = 1000;
const H = 100;
/** A gap longer than this (container stopped, sampler down) breaks the line instead of bridging it. */
const GAP_MS = 3 * 60_000;

/** Rounds up to 1/2/2.5/5 x 10^n so the top gridline lands on a readable value. */
function niceMax(v: number): number {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  return ([1, 2, 2.5, 5, 10].find((m) => m * p >= v) ?? 10) * p;
}

/**
 * Dependency-free single-series line chart (one measure, one axis) with a crosshair tooltip.
 * The SVG stretches to its box (non-scaling strokes keep the line 2px); text lives in HTML so it never distorts.
 */
@Component({
  selector: 'app-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <span class="label">{{ label() }}</span>
      @if (latest(); as l) { <span class="value">{{ format()(l.v) }}</span> }
    </div>
    <div class="plot" (pointermove)="hover($event)" (pointerleave)="hovered.set(null)"
      role="img" [attr.aria-label]="label() + ', ' + points().length + ' points' + (latest() ? ', latest ' + format()(latest()!.v) : '')">
      <span class="y-max">{{ format()(yMax()) }}</span>
      <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" preserveAspectRatio="none" aria-hidden="true">
        <line class="grid" x1="0" [attr.x2]="W" y1="0.5" y2="0.5" />
        <line class="grid" x1="0" [attr.x2]="W" [attr.y1]="H / 2" [attr.y2]="H / 2" />
        <line class="grid base" x1="0" [attr.x2]="W" [attr.y1]="H - 0.5" [attr.y2]="H - 0.5" />
        @for (seg of segments(); track $index) {
          <path class="area" [attr.d]="seg.area" />
          <path class="line" [attr.d]="seg.line" />
        }
      </svg>
      @if (hovered(); as h) {
        <div class="crosshair" [style.left.%]="h.x"></div>
        <div class="dot" [style.left.%]="h.x" [style.top.%]="h.y"></div>
        <div class="tip" [style.left.%]="h.x" [class.flip]="h.x > 60">
          <span class="when">{{ timeLabel(h.p.t) }}</span>
          <strong>{{ format()(h.p.v) }}</strong>
        </div>
      }
      @if (points().length === 0) { <span class="empty">No data yet</span> }
    </div>
    <div class="x-axis"><span>{{ timeLabel(from()) }}</span><span>{{ timeLabel(to()) }}</span></div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 4px; min-width: 0; --series: var(--mat-sys-primary); }
    .head { display: flex; justify-content: space-between; align-items: baseline; font-size: 12px; color: var(--mat-sys-on-surface-variant); }
    .value { color: var(--mat-sys-on-surface); font-weight: 600; font-variant-numeric: tabular-nums; }
    .plot { position: relative; height: 72px; cursor: crosshair; touch-action: none; }
    svg { display: block; width: 100%; height: 100%; overflow: visible; }
    .grid { stroke: var(--mat-sys-outline-variant); stroke-width: 1; vector-effect: non-scaling-stroke; opacity: 0.6; }
    .grid.base { opacity: 1; }
    .line { fill: none; stroke: var(--series); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; vector-effect: non-scaling-stroke; }
    .area { fill: var(--series); opacity: 0.1; stroke: none; }
    .y-max { position: absolute; top: 2px; left: 2px; font-size: 10px; color: var(--mat-sys-on-surface-variant); pointer-events: none; }
    .empty { position: absolute; inset: 0; display: grid; place-items: center; font-size: 12px; color: var(--mat-sys-on-surface-variant); }
    .crosshair { position: absolute; top: 0; bottom: 0; width: 1px; background: var(--mat-sys-on-surface-variant); opacity: 0.5; pointer-events: none; }
    .dot { position: absolute; width: 8px; height: 8px; margin: -5px 0 0 -5px; border-radius: 50%; background: var(--series); border: 1px solid var(--mat-sys-surface); pointer-events: none; }
    .tip {
      position: absolute; top: -6px; transform: translate(8px, -100%); white-space: nowrap; pointer-events: none; z-index: 2;
      display: flex; gap: 6px; align-items: baseline; padding: 3px 8px; border-radius: 6px; font-size: 11.5px;
      background: var(--mat-sys-inverse-surface); color: var(--mat-sys-inverse-on-surface);
      &.flip { transform: translate(calc(-100% - 8px), -100%); }
      .when { opacity: 0.8; }
    }
    .x-axis { display: flex; justify-content: space-between; font-size: 10px; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class LineChart {
  protected readonly W = W;
  protected readonly H = H;

  readonly points = input.required<ChartPoint[]>();
  readonly label = input.required<string>();
  readonly format = input<(v: number) => string>((v) => v.toFixed(1));
  /** X domain (epoch ms); the chart spans the whole requested range, not just the data. */
  /** Smallest top of the y axis, so near-zero series (an idle container's CPU) don't magnify noise. */
  readonly floor = input(0);
  readonly from = input.required<number>();
  readonly to = input.required<number>();

  protected readonly hovered = signal<{ x: number; y: number; p: ChartPoint } | null>(null);

  protected readonly latest = computed(() => this.points().at(-1) ?? null);
  protected readonly yMax = computed(() => niceMax(Math.max(this.floor(), Math.max(0, ...this.points().map((p) => p.v)) * 1.1)));

  private x(t: number): number {
    const span = this.to() - this.from() || 1;
    return ((t - this.from()) / span) * W;
  }

  private y(v: number): number {
    return H - (v / this.yMax()) * H;
  }

  protected readonly segments = computed(() => {
    const runs: ChartPoint[][] = [];
    for (const p of this.points()) {
      const run = runs.at(-1);
      if (run && p.t - run[run.length - 1].t <= GAP_MS) run.push(p);
      else runs.push([p]);
    }
    return runs.map((run) => {
      const coords = run.map((p) => `${this.x(p.t).toFixed(1)},${this.y(p.v).toFixed(2)}`);
      // A lone point still needs a visible mark: draw it as a short flat stub.
      if (coords.length === 1) coords.push(`${(this.x(run[0].t) + 4).toFixed(1)},${this.y(run[0].v).toFixed(2)}`);
      const line = 'M' + coords.join('L');
      const first = coords[0].split(',')[0];
      const last = coords[coords.length - 1].split(',')[0];
      return { line, area: `${line}L${last},${H}L${first},${H}Z` };
    });
  });

  /** Snaps the crosshair to the data point nearest the pointer's time. */
  protected hover(event: PointerEvent): void {
    const pts = this.points();
    if (!pts.length) return;
    const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const t = this.from() + ((event.clientX - box.left) / box.width) * (this.to() - this.from());
    let best = pts[0];
    for (const p of pts) if (Math.abs(p.t - t) < Math.abs(best.t - t)) best = p;
    this.hovered.set({ x: (this.x(best.t) / W) * 100, y: (this.y(best.v) / H) * 100, p: best });
  }

  protected timeLabel(t: number): string {
    return new Date(t).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }
}
