import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { EnvLine } from './env-parse';

export interface EnvImportData {
  /** The .env's path in the project, for the title. */
  path: string;
  lines: EnvLine[];
  /** Names the environment already has: importing replaces them. */
  existing: string[];
}

export interface EnvImportRow extends EnvLine {
  include: boolean;
}

export interface EnvImportResult {
  rows: EnvImportRow[];
  removeFromFile: boolean;
}

/** Picks which .env lines become DockiUp variables or secrets, and whether they leave the file. */
@Component({
  selector: 'app-env-import-dialog',
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .note { margin: 0 0 12px; color: var(--mat-sys-on-surface-variant); }
    .grid { display: grid; grid-template-columns: auto minmax(140px, 1fr) minmax(120px, 1.2fr) auto minmax(100px, 0.8fr); gap: 6px 10px; align-items: center; }
    .head { font-size: 12px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--mat-sys-on-surface-variant); }
    .name { font: 13px var(--x-mono); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .value { font: 13px var(--x-mono); color: var(--mat-sys-on-surface-variant); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .replaces { color: var(--x-warn); font: 12px inherit; margin-left: 6px; }
    .x-group button { height: 32px; padding: 0 12px; }
    .group { box-sizing: border-box; height: 32px; padding: 0 10px; border: 0; border-radius: 10px; background: var(--mat-sys-surface-container-highest);
      color: var(--mat-sys-on-surface); font: inherit; font-size: 13px; min-width: 0; }
    .remove { margin-top: 12px; display: block; }
  `,
  template: `
    <h2 mat-dialog-title>Import {{ data.path }}</h2>
    <mat-dialog-content>
      <p class="note">Pick what DockiUp should keep. Secrets are encrypted and never shown again; variables stay readable. Groups come from the file's section comments.</p>
      <div class="grid">
        <mat-checkbox [checked]="allIncluded()" [indeterminate]="!allIncluded() && someIncluded()" (change)="setAll($event.checked)" aria-label="Import all" />
        <span class="head">Name</span><span class="head">Value</span><span class="head">Type</span><span class="head">Group</span>
        @for (row of rows(); track row.name) {
          <mat-checkbox [(ngModel)]="row.include" (ngModelChange)="touch()" [aria-label]="'Import ' + row.name" />
          <span class="name" [title]="row.name">{{ row.name }}@if (data.existing.includes(row.name)) {<span class="replaces">replaces</span>}</span>
          <span class="value">{{ row.secret ? '••••••••' : row.value || '(empty)' }}</span>
          <span class="x-group" role="group" [attr.aria-label]="row.name + ' type'">
            <button type="button" [matButton]="row.secret ? 'tonal' : 'filled'" [class.x-on]="!row.secret" (click)="row.secret = false; touch()">Variable</button>
            <button type="button" [matButton]="row.secret ? 'filled' : 'tonal'" [class.x-on]="row.secret" (click)="row.secret = true; touch()">Secret</button>
          </span>
          <input class="group" [(ngModel)]="row.group" placeholder="None" [attr.aria-label]="row.name + ' group'" />
        }
      </div>
      <mat-checkbox class="remove" [(ngModel)]="removeFromFile">Remove the imported lines from {{ data.path }}</mat-checkbox>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">Cancel</button>
      <button mat-flat-button [disabled]="!someIncluded()" [mat-dialog-close]="result()">Import {{ count() }}</button>
    </mat-dialog-actions>
  `,
})
export class EnvImportDialog {
  protected readonly data = inject<EnvImportData>(MAT_DIALOG_DATA);
  protected readonly rows = signal<EnvImportRow[]>(this.data.lines.map((l) => ({ ...l, include: true })));
  protected removeFromFile = true;

  // Rows are edited in place through ngModel; bumping the signal refreshes the counts.
  protected touch(): void {
    this.rows.update((r) => [...r]);
  }

  protected readonly count = computed(() => this.rows().filter((r) => r.include).length);
  protected readonly someIncluded = computed(() => this.count() > 0);
  protected readonly allIncluded = computed(() => this.count() === this.rows().length);

  protected setAll(include: boolean): void {
    this.rows.update((r) => r.map((x) => ({ ...x, include })));
  }

  protected result(): EnvImportResult {
    return { rows: this.rows().filter((r) => r.include), removeFromFile: this.removeFromFile };
  }
}
