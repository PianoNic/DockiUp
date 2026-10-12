import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { EnvironmentEntryDto, EnvironmentEntryInput, EnvironmentKind, ProjectEnvironmentService, ProjectFilesService, SecretDto, SecretsService } from '../api';
import { NotificationService, errorMessage } from '../shared/services/notification.service';
import { EnvImportData, EnvImportDialog, EnvImportResult } from './env-import-dialog';
import { parseEnv, removeFromEnv } from './env-parse';

interface Row {
  name: string;
  group: string;
  kind: EnvironmentKind;
  value: string;
  /** A stored secret's name when loaded: an empty value then keeps it (also across a rename). */
  keepFrom: string | null;
  vaultSecretId: string | null;
  vaultSecretName: string | null;
}

const NO_GROUP = 'No group';

/** The project's Environment tab: variables and secrets, grouped, plus importing an existing .env. */
@Component({
  selector: 'app-project-environment',
  imports: [FormsModule, MatButtonModule, MatIconModule, MatMenuModule, MatSelectModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host { display: flex; flex-direction: column; gap: 12px; height: 100%; min-height: 0; }
    .toolbar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; flex-shrink: 0; }
    .grow { flex: 1; }
    .hint { margin: 0; color: var(--mat-sys-on-surface-variant); font-size: 14px; flex-shrink: 0; }
    code { font-family: var(--x-mono); font-size: 13px; }
    .groups { flex: 1; min-height: 0; overflow: auto; display: flex; flex-direction: column; gap: 14px; padding-bottom: 8px; }
    .group-title { margin: 0 0 6px 4px; font-size: 15px; font-weight: 700; }
    .group-title .count { font-weight: 500; color: var(--mat-sys-on-surface-variant); margin-left: 6px; }
    .x-row { gap: 10px; padding: 8px 10px 8px 14px; min-height: 56px; }
    .field { box-sizing: border-box; height: 40px; padding: 0 14px; border: 0; border-radius: 12px; min-width: 0;
      background: var(--mat-sys-surface-container-highest); color: var(--mat-sys-on-surface); font: inherit; font-size: 14px; }
    .field:focus { outline: 2px solid var(--mat-sys-primary); outline-offset: 1px; }
    .name { width: 240px; font-family: var(--x-mono); font-size: 13px; }
    .value { flex: 1; font-family: var(--x-mono); font-size: 13px; }
    .kind { flex-shrink: 0; color: var(--mat-sys-on-surface-variant); }
    .kind.secret { color: var(--mat-sys-primary); }
    .group-slot { flex-shrink: 0; width: 150px; display: flex; }
    .group-chip { border: 0; cursor: pointer; font-family: inherit; max-width: 160px; overflow: hidden; text-overflow: ellipsis; }
    .group-chip:hover { background: var(--mat-sys-surface-container-high); }
    .new-group { padding: 6px 12px 8px; }
    .new-group .field { width: 100%; }
    .vault { flex: 1; min-width: 0; display: flex; align-items: center; }
    .empty { margin: 24px 4px; color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 900px) { .x-row { flex-wrap: wrap; } .name { width: auto; flex: 1; } .value, .vault { flex-basis: 100%; } }
  `,
  template: `
    <p class="hint">
      Written with the project's <code>.env</code> into the env file compose reads at deploy time; use them in the compose file
      as <code>{{ example }}</code>. Secrets are encrypted, never shown again, and hidden in deploy logs. Changes apply on the next deploy.
    </p>
    <div class="toolbar">
      <button matButton="tonal" (click)="importEnv()" [disabled]="busy()"><mat-icon>upload_file</mat-icon> Import .env</button>
      <span class="grow"></span>
      <button matButton="tonal" (click)="add(Variable)"><mat-icon>add</mat-icon> Variable</button>
      <button matButton="tonal" (click)="add(Secret)"><mat-icon>enhanced_encryption</mat-icon> Secret</button>
      <button matButton="tonal" (click)="add(Vault)" [disabled]="!vault().length" matTooltip="Use a secret shared through the vault (Settings → Vault)"><mat-icon>lock</mat-icon> From vault</button>
      <button mat-flat-button (click)="save()" [disabled]="!dirty() || busy()"><mat-icon>save</mat-icon> {{ busy() ? 'Saving…' : 'Save' }}</button>
    </div>

    <!-- One menu for every row's group: existing groups, a new one, or none. -->
    <mat-menu #groupMenu="matMenu">
      @for (g of groupNames(); track g) {
        @if (g !== menuRow?.group) { <button mat-menu-item (click)="setGroup(g)"><mat-icon>label</mat-icon>{{ g }}</button> }
      }
      <div class="new-group" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()">
        <input #newGroup class="field" placeholder="New group…" aria-label="New group"
          (keydown.enter)="setGroup(newGroup.value); newGroup.value = ''; menuTrigger?.closeMenu()" />
      </div>
      @if (menuRow?.group) { <button mat-menu-item (click)="setGroup('')"><mat-icon>label_off</mat-icon>Remove from group</button> }
    </mat-menu>

    <div class="groups">
      @for (g of grouped(); track g.name) {
        <section>
          <h3 class="group-title">{{ g.name }}<span class="count">{{ g.rows.length }}</span></h3>
          <div class="x-list">
            @for (row of g.rows; track row) {
              <div class="x-row">
                <mat-icon class="kind" [class.secret]="row.kind !== Variable" [matTooltip]="kindLabel(row)" [attr.aria-label]="kindLabel(row)">{{ kindIcon(row) }}</mat-icon>
                <input class="field name" [(ngModel)]="row.name" (ngModelChange)="dirty.set(true)" placeholder="NAME" spellcheck="false" aria-label="Name" />
                @if (row.kind === Vault) {
                  <mat-select class="field vault" [(ngModel)]="row.vaultSecretId" (ngModelChange)="dirty.set(true)" placeholder="Pick a vault secret" aria-label="Vault secret">
                    @for (s of vault(); track s.id) { <mat-option [value]="s.id">{{ s.name }}</mat-option> }
                  </mat-select>
                } @else {
                  <input class="field value" [type]="row.kind === Secret ? 'password' : 'text'" [(ngModel)]="row.value" (ngModelChange)="dirty.set(true)"
                    [placeholder]="row.kind === Secret && row.keepFrom ? 'Stored · type to replace' : 'Value'" spellcheck="false" autocomplete="off" aria-label="Value" />
                }
                <span class="group-slot">
                @if (row.group) {
                  <button class="x-chip group-chip" [matMenuTriggerFor]="groupMenu" #t="matMenuTrigger" (click)="openGroupMenu(row, t)"
                    [attr.aria-label]="'Group ' + row.group + ', change'"><mat-icon>label</mat-icon>{{ row.group }}</button>
                } @else {
                  <button mat-icon-button [matMenuTriggerFor]="groupMenu" #t="matMenuTrigger" (click)="openGroupMenu(row, t)"
                    matTooltip="Add to a group" aria-label="Add to a group"><mat-icon>new_label</mat-icon></button>
                }
                </span>
                <button mat-icon-button (click)="remove(row)" matTooltip="Remove" aria-label="Remove"><mat-icon>delete</mat-icon></button>
              </div>
            }
          </div>
        </section>
      } @empty {
        <p class="empty">No variables yet. Add one, or import the project's <code>.env</code>.</p>
      }
    </div>
  `,
})
export class ProjectEnvironment {
  readonly projectId = input.required<string>();

  private readonly api = inject(ProjectEnvironmentService);
  private readonly files = inject(ProjectFilesService);
  private readonly secrets = inject(SecretsService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(NotificationService);

  protected readonly Variable = EnvironmentKind.Variable;
  protected readonly Secret = EnvironmentKind.Secret;
  protected readonly Vault = EnvironmentKind.Vault;
  protected readonly example = '${NAME}';

  protected readonly rows = signal<Row[]>([]);
  /** Shared vault secrets a From vault entry can use. */
  protected readonly vault = signal<SecretDto[]>([]);
  protected readonly dirty = signal(false);
  protected readonly busy = signal(false);

  /** Named groups alphabetically, ungrouped last; rows by name. */
  protected readonly grouped = computed(() => {
    const map = new Map<string, Row[]>();
    for (const row of this.rows()) {
      const key = row.group.trim() || NO_GROUP;
      map.set(key, [...(map.get(key) ?? []), row]);
    }
    return [...map.entries()]
      .sort(([a], [b]) => (a === NO_GROUP ? 1 : b === NO_GROUP ? -1 : a.localeCompare(b)))
      .map(([name, rows]) => ({ name, rows }));
  });

  protected readonly groupNames = computed(() => [...new Set(this.rows().map((r) => r.group.trim()).filter((g) => g))].sort());

  constructor() {
    effect(() => {
      const id = this.projectId();
      firstValueFrom(this.api.getProjectEnvironment(id))
        .then((entries) => this.apply(entries))
        .catch((err) => this.toast.showError('Failed to load the environment', err));
    });
    firstValueFrom(this.secrets.apiSecretsGet()).then((v) => this.vault.set(v)).catch(() => this.vault.set([]));
  }

  private apply(entries: EnvironmentEntryDto[]): void {
    this.rows.set(entries.map((e) => ({
      name: e.name, group: e.group ?? '', kind: e.kind, value: e.value ?? '',
      keepFrom: e.kind === EnvironmentKind.Secret ? e.name : null, vaultSecretId: e.vaultSecretId, vaultSecretName: e.vaultSecretName,
    })));
    this.dirty.set(false);
  }

  protected add(kind: EnvironmentKind): void {
    this.rows.update((r) => [...r, { name: '', group: '', kind, value: '', keepFrom: null, vaultSecretId: null, vaultSecretName: null }]);
    this.dirty.set(true);
  }

  protected remove(row: Row): void {
    this.rows.update((r) => r.filter((x) => x !== row));
    this.dirty.set(true);
  }

  protected kindIcon(row: Row): string {
    return row.kind === EnvironmentKind.Secret ? 'lock' : row.kind === EnvironmentKind.Vault ? 'key' : 'text_fields';
  }

  protected kindLabel(row: Row): string {
    return row.kind === EnvironmentKind.Secret ? 'Secret' : row.kind === EnvironmentKind.Vault ? 'From the vault' : 'Variable';
  }

  /** The row the group menu was opened for, and its trigger (to close it after typing a new group). */
  protected menuRow: Row | null = null;
  protected menuTrigger: MatMenuTrigger | null = null;

  protected openGroupMenu(row: Row, trigger: MatMenuTrigger): void {
    this.menuRow = row;
    this.menuTrigger = trigger;
  }

  // The row then moves to its group's section.
  protected setGroup(group: string): void {
    if (!this.menuRow) return;
    this.menuRow.group = group.trim();
    this.rows.update((r) => [...r]);
    this.dirty.set(true);
  }

  private toInput(row: Row): EnvironmentEntryInput {
    const keep = row.kind === EnvironmentKind.Secret && !!row.keepFrom && row.value === '';
    return {
      name: row.name.trim(), group: row.group.trim() || null, kind: row.kind,
      value: row.kind === EnvironmentKind.Vault || keep ? null : row.value,
      keepFrom: keep ? row.keepFrom : null, vaultSecretId: row.vaultSecretId,
    };
  }

  protected async save(): Promise<boolean> {
    const rows = this.rows();
    if (rows.some((r) => !r.name.trim())) {
      this.toast.error('Every variable needs a name');
      return false;
    }
    if (rows.some((r) => r.kind === EnvironmentKind.Vault && !r.vaultSecretId)) {
      this.toast.error('Pick a vault secret for every From vault entry');
      return false;
    }
    this.busy.set(true);
    try {
      this.apply(await firstValueFrom(this.api.setProjectEnvironment(this.projectId(), rows.map((r) => this.toInput(r)))));
      this.toast.success('Environment saved; it applies on the next deploy');
      return true;
    } catch (err) {
      this.toast.showError('Could not save the environment', err);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  /** Reads the .env next to the compose file, lets the user pick, saves, and optionally strips it from the file. */
  protected async importEnv(): Promise<void> {
    const id = this.projectId();
    let path = '.env';
    let content: string;
    try {
      const compose = (await firstValueFrom(this.files.listProjectFiles(id))).composeFile ?? '';
      path = compose.includes('/') ? `${compose.slice(0, compose.lastIndexOf('/'))}/.env` : '.env';
      content = (await firstValueFrom(this.files.readProjectFile(id, path))).content;
    } catch (err) {
      this.toast.error(`No ${path} to import`, errorMessage(err));
      return;
    }
    const lines = parseEnv(content);
    if (!lines.length) {
      this.toast.error(`${path} has no variables`);
      return;
    }

    const data: EnvImportData = { path, lines, existing: this.rows().map((r) => r.name.trim()) };
    const result = await firstValueFrom(this.dialog.open<EnvImportDialog, EnvImportData, EnvImportResult | null>(EnvImportDialog, { data, width: '920px', maxWidth: '95vw' }).afterClosed());
    if (!result?.rows.length) return;

    const imported = new Set(result.rows.map((r) => r.name));
    this.rows.update((r) => [
      ...r.filter((x) => !imported.has(x.name.trim())),
      ...result.rows.map((l) => ({
        name: l.name, group: l.group ?? '', kind: l.secret ? EnvironmentKind.Secret : EnvironmentKind.Variable,
        value: l.value, keepFrom: null, vaultSecretId: null, vaultSecretName: null,
      })),
    ]);
    if (!(await this.save())) return;

    if (result.removeFromFile) {
      try {
        await firstValueFrom(this.files.saveProjectFile(id, { path, content: removeFromEnv(content, imported), deploy: false }));
        this.toast.success(`Imported ${imported.size} and removed them from ${path}`);
      } catch (err) {
        this.toast.showError(`Imported, but could not update ${path}`, err);
      }
    }
  }
}
