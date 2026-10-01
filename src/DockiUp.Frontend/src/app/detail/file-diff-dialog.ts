import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface DiffLine {
  kind: ' ' | '+' | '-' | '…';
  text: string;
}

export interface FileDiffData {
  path: string;
  before: string;
  after: string;
  deploy: boolean;
  /** Shown above the diff: what saving will do (commit and push, or not versioned). */
  note: string;
}

/** Line diff with a few lines of context around each change. Common head/tail are trimmed first; the
 * middle gets an LCS when small enough, otherwise it is shown as replaced wholesale. */
export function lineDiff(before: string, after: string, context = 3): DiffLine[] {
  const a = before.split('\n');
  const b = after.split('\n');
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) start++;
  let endA = a.length;
  let endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) { endA--; endB--; }

  const midA = a.slice(start, endA);
  const midB = b.slice(start, endB);
  const middle: DiffLine[] = [];
  if (midA.length * midB.length <= 4_000_000) {
    // lcs[i][j] = LCS length of midA[i..] and midB[j..]
    const lcs = Array.from({ length: midA.length + 1 }, () => new Uint32Array(midB.length + 1));
    for (let i = midA.length - 1; i >= 0; i--)
      for (let j = midB.length - 1; j >= 0; j--)
        lcs[i][j] = midA[i] === midB[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
    let i = 0;
    let j = 0;
    while (i < midA.length || j < midB.length) {
      if (i < midA.length && j < midB.length && midA[i] === midB[j]) { middle.push({ kind: ' ', text: midA[i++] }); j++; }
      else if (j < midB.length && (i === midA.length || lcs[i][j + 1] >= lcs[i + 1][j])) middle.push({ kind: '+', text: midB[j++] });
      else middle.push({ kind: '-', text: midA[i++] });
    }
  } else {
    midA.forEach((text) => middle.push({ kind: '-', text }));
    midB.forEach((text) => middle.push({ kind: '+', text }));
  }

  const all: DiffLine[] = [
    ...a.slice(0, start).map((text) => ({ kind: ' ' as const, text })),
    ...middle,
    ...a.slice(endA).map((text) => ({ kind: ' ' as const, text })),
  ];
  // Keep only lines near a change; collapse the rest into one marker per gap.
  const near = all.map(() => false);
  all.forEach((line, i) => {
    if (line.kind === ' ') return;
    for (let k = Math.max(0, i - context); k <= Math.min(all.length - 1, i + context); k++) near[k] = true;
  });
  const out: DiffLine[] = [];
  all.forEach((line, i) => {
    if (near[i]) out.push(line);
    else if (out.at(-1)?.kind !== '…') out.push({ kind: '…', text: '' });
  });
  return out;
}

/** Shows what a save changes before writing it. Closes with true to save. */
@Component({
  selector: 'app-file-diff-dialog',
  imports: [MatButtonModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .note { margin: 0 0 12px; color: var(--mat-sys-on-surface-variant); }
    .stats { margin-left: 8px; font-size: 14px; }
    .add { color: #43a047; }
    .del { color: #e53935; }
    .diff { margin: 0; max-height: 60vh; overflow: auto; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px;
      font: 12.5px/1.5 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
    .row { display: block; padding: 0 10px; white-space: pre; }
    .row[data-kind='+'] { background: color-mix(in srgb, #43a047 16%, transparent); }
    .row[data-kind='-'] { background: color-mix(in srgb, #e53935 16%, transparent); }
    .row[data-kind='…'] { color: var(--mat-sys-outline); background: var(--mat-sys-surface-container); }
  `,
  template: `
    <h2 mat-dialog-title>
      {{ data.deploy ? 'Save and deploy' : 'Save' }} {{ data.path }}
      <span class="stats"><span class="add">+{{ added }}</span> <span class="del">-{{ removed }}</span></span>
    </h2>
    <mat-dialog-content>
      <p class="note">{{ data.note }}</p>
      @if (lines.length === 0) {
        <p>No changes.</p>
      } @else {
        <pre class="diff">@for (line of lines; track $index) {<span class="row" [attr.data-kind]="line.kind">{{ line.kind === '…' ? '…' : line.kind + ' ' + line.text }}</span>}</pre>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">Cancel</button>
      <button mat-flat-button [mat-dialog-close]="true" cdkFocusInitial>{{ data.deploy ? 'Save and deploy' : 'Save' }}</button>
    </mat-dialog-actions>
  `,
})
export class FileDiffDialog {
  protected readonly data = inject<FileDiffData>(MAT_DIALOG_DATA);
  protected readonly lines = lineDiff(this.data.before, this.data.after);
  protected readonly added = this.lines.filter((l) => l.kind === '+').length;
  protected readonly removed = this.lines.filter((l) => l.kind === '-').length;
}
