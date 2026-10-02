import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import type { Database } from 'sql.js';

/** Rows shown per query; a bigger result is cut off with a note. */
const MAX_ROWS = 500;

/** True for the file names the Files tab opens in the SQLite viewer instead of the editor. */
export function isSqlite(path: string): boolean {
  return /\.(db|db3|sqlite|sqlite3)$/i.test(path);
}

interface Result {
  columns: string[];
  rows: unknown[][];
  truncated: boolean;
}

/**
 * Read-only SQLite browser. The file's bytes are opened in the browser with sql.js (SQLite as WebAssembly,
 * loaded on first use; the .wasm is self-hosted under /sqljs), so it works the same for files on nodes.
 * Queries run against that in-memory copy: nothing is ever written back to the server.
 */
@Component({
  selector: 'app-sqlite-viewer',
  imports: [FormsModule, MatButtonModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error(); as e) { <p class="hint error"><mat-icon>error</mat-icon>{{ e }}</p> }
    @if (tables().length) {
      <div class="tables" role="tablist" aria-label="Tables">
        @for (t of tables(); track t) {
          <button class="x-chip" role="tab" [class.active]="t === table()" [attr.aria-selected]="t === table()" (click)="showTable(t)">{{ t }}</button>
        }
      </div>
    } @else if (!error()) {
      <p class="hint">{{ db ? 'This database has no tables.' : 'Opening…' }}</p>
    }
    <form class="query" (ngSubmit)="run()">
      <input class="sql" name="sql" [(ngModel)]="sql" spellcheck="false" aria-label="SQL query" placeholder="SELECT * FROM …" />
      <button mat-flat-button type="submit" [disabled]="!db || !sql.trim()"><mat-icon>play_arrow</mat-icon> Run</button>
    </form>
    @if (result(); as r) {
      <div class="grid x-code">
        <table>
          <thead><tr>@for (c of r.columns; track $index) { <th>{{ c }}</th> }</tr></thead>
          <tbody>
            @for (row of r.rows; track $index) {
              <tr>@for (v of row; track $index) { <td [class.null]="v === null">{{ show(v) }}</td> }</tr>
            } @empty {
              <tr><td class="null" [attr.colspan]="r.columns.length || 1">No rows</td></tr>
            }
          </tbody>
        </table>
      </div>
      <p class="hint">{{ r.rows.length }}{{ r.truncated ? '+' : '' }} row{{ r.rows.length === 1 ? '' : 's' }}{{ r.truncated ? ', showing the first ' + max : '' }} · read-only copy, changes are not saved</p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 8px; min-height: 0; flex: 1; }
    .tables { display: flex; gap: 6px; overflow-x: auto; flex-shrink: 0; padding-bottom: 2px; }
    .tables .x-chip { border: 0; cursor: pointer; font-family: inherit; }
    .tables .x-chip.active { background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); }
    .query { display: flex; gap: 8px; align-items: center; }
    .sql {
      flex: 1; min-width: 0; height: 40px; box-sizing: border-box; padding: 0 16px; border: 0; border-radius: 20px;
      background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface); font: 14px var(--x-mono);
      outline-offset: 2px;
    }
    .grid { flex: 1; min-height: 0; overflow: auto; padding: 12px 16px; }
    table { border-collapse: collapse; font: 13px/1.4 var(--x-mono); }
    th { position: sticky; top: -12px; background: #1d2433; text-align: left; color: #9fb0d9; font-weight: 600; }
    th, td { padding: 6px 12px; white-space: nowrap; max-width: 420px; overflow: hidden; text-overflow: ellipsis; border-bottom: 1px solid rgba(219, 226, 249, 0.1); }
    .null { color: #7d8aa8; font-style: italic; }
    .hint { display: flex; align-items: center; gap: 6px; margin: 0; color: var(--mat-sys-on-surface-variant); font-size: 13px; }
    .error { color: var(--mat-sys-error); }
  `,
})
export class SqliteViewer {
  /** The database file's bytes. */
  readonly bytes = input.required<Uint8Array>();

  protected readonly max = MAX_ROWS;
  protected readonly tables = signal<string[]>([]);
  protected readonly table = signal<string | null>(null);
  protected readonly result = signal<Result | null>(null);
  protected readonly error = signal<string | null>(null);
  protected sql = '';
  protected db: Database | null = null;

  constructor() {
    effect(() => void this.openDb(this.bytes()));
    inject(DestroyRef).onDestroy(() => this.db?.close());
  }

  private async openDb(bytes: Uint8Array): Promise<void> {
    this.db?.close();
    this.db = null;
    this.tables.set([]);
    this.result.set(null);
    this.error.set(null);
    try {
      const { default: initSqlJs } = await import('sql.js');
      const SQL = await initSqlJs({ locateFile: (f) => new URL(`sqljs/${f}`, document.baseURI).href });
      this.db = new SQL.Database(bytes);
      const names = this.db.exec(`SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name`);
      this.tables.set((names[0]?.values ?? []).map((r) => String(r[0])));
      if (this.tables().length) this.showTable(this.tables()[0]);
    } catch (err) {
      this.error.set(`Can't open this file as a SQLite database: ${err instanceof Error ? err.message : err}`);
    }
  }

  protected showTable(name: string): void {
    this.table.set(name);
    this.sql = `SELECT * FROM "${name.replace(/"/g, '""')}" LIMIT ${MAX_ROWS}`;
    this.run();
  }

  protected run(): void {
    if (!this.db) return;
    this.error.set(null);
    let stmt;
    try {
      stmt = this.db.prepare(this.sql);
      const rows: unknown[][] = [];
      let truncated = false;
      while (stmt.step()) {
        if (rows.length === MAX_ROWS) { truncated = true; break; }
        rows.push(stmt.get());
      }
      this.result.set({ columns: stmt.getColumnNames(), rows, truncated });
    } catch (err) {
      this.result.set(null);
      this.error.set(err instanceof Error ? err.message : String(err));
    } finally {
      stmt?.free();
    }
  }

  protected show(v: unknown): string {
    if (v === null) return 'NULL';
    if (v instanceof Uint8Array) return `BLOB (${v.length} bytes)`;
    return String(v);
  }
}
