import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { DeploymentDto, ProjectFileEntryDto, ProjectFilesDto, ProjectFilesService } from '../api';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { NotificationService } from '../shared/services/notification.service';
import { CodeEditor, languageFor } from './code-editor';
import { FileDiffData, FileDiffDialog } from './file-diff-dialog';

const MAX_UPLOAD_BYTES = 25 * 1024 * 1024;

interface OpenFile {
  path: string;
  /** Content as last loaded or saved; the diff and the dirty flag compare against it. */
  original: string;
  tracked: boolean;
}

/** Browse, upload, download, delete and edit the files in a DockiUp project's folder (on its host). */
@Component({
  selector: 'app-project-files',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, MatTooltipModule, LocalDatePipe, CodeEditor],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './project-files.html',
  styleUrl: './project-files.scss',
})
export class ProjectFiles {
  readonly projectId = input.required<string>();
  /** A save queued a deployment (the page switches to the Deployments tab). */
  readonly deployed = output<DeploymentDto>();

  private readonly api = inject(ProjectFilesService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);

  protected readonly listing = signal<ProjectFilesDto | null>(null);
  protected readonly path = signal('');
  protected readonly busy = signal(false);
  protected readonly dragging = signal(false);
  /** Inline "new folder / new file" name field. */
  protected readonly creating = signal<'folder' | 'file' | null>(null);
  protected newName = '';

  protected readonly file = signal<OpenFile | null>(null);
  protected readonly content = signal('');
  protected readonly dirty = computed(() => this.file() !== null && this.content() !== this.file()!.original);
  protected readonly language = computed(() => languageFor(this.file()?.path ?? ''));
  protected readonly isCompose = computed(() => !!this.file() && this.file()!.path === this.listing()?.composeFile);

  protected readonly crumbs = computed(() => {
    const parts = this.path() ? this.path().split('/') : [];
    return parts.map((name, i) => ({ name, path: parts.slice(0, i + 1).join('/') }));
  });

  constructor() {
    effect(() => {
      this.projectId();
      untracked(() => {
        this.file.set(null);
        void this.load('');
      });
    });
  }

  protected async load(path: string): Promise<void> {
    try {
      this.listing.set(await firstValueFrom(this.api.listProjectFiles(this.projectId(), path || undefined)));
      this.path.set(path);
    } catch (err) {
      this.notifications.showError('Failed to list files', err);
    }
  }

  protected async open(entry: ProjectFileEntryDto): Promise<void> {
    if (entry.isDirectory) {
      await this.load(entry.path);
      return;
    }
    if (!(await this.canLeaveFile())) return;
    try {
      const f = await firstValueFrom(this.api.readProjectFile(this.projectId(), entry.path));
      this.file.set({ path: f.path, original: f.content, tracked: f.tracked });
      this.content.set(f.content);
    } catch (err) {
      this.notifications.showError(`Can't open ${entry.name}`, err);
    }
  }

  protected async closeFile(): Promise<void> {
    if (await this.canLeaveFile()) this.file.set(null);
  }

  private async canLeaveFile(): Promise<boolean> {
    return (
      !this.dirty() ||
      this.confirm.ask({
        title: 'Discard changes?',
        message: `Your changes to ${this.file()!.path} are not saved.`,
        confirmText: 'Discard',
        destructive: true,
      })
    );
  }

  private join(name: string): string {
    return this.path() ? `${this.path()}/${name}` : name;
  }

  protected startCreate(kind: 'folder' | 'file'): void {
    this.newName = '';
    this.creating.set(kind);
  }

  protected async create(): Promise<void> {
    const name = this.newName.trim();
    const kind = this.creating();
    if (!name || !kind) return;
    const path = this.join(name);
    if (kind === 'file') {
      // Nothing is written until the first save.
      if (!(await this.canLeaveFile())) return;
      this.creating.set(null);
      this.file.set({ path, original: '', tracked: false });
      this.content.set('');
      return;
    }
    try {
      await firstValueFrom(this.api.createProjectFolder(this.projectId(), path));
      this.creating.set(null);
      await this.load(path);
    } catch (err) {
      this.notifications.showError('Failed to create folder', err);
    }
  }

  protected async upload(files: FileList | null | undefined): Promise<void> {
    if (!files?.length) return;
    this.busy.set(true);
    try {
      for (const f of Array.from(files)) {
        if (f.size > MAX_UPLOAD_BYTES) {
          this.notifications.error(`${f.name} is too large`, 'the limit is 25 MB');
          continue;
        }
        try {
          const result = await firstValueFrom(this.api.uploadProjectFile(this.projectId(), f, this.path() || undefined));
          this.notifications.success(result.commit ? `Uploaded ${f.name} and committed ${result.commit.slice(0, 7)}` : `Uploaded ${f.name}`);
        } catch (err) {
          this.notifications.showError(`Failed to upload ${f.name}`, err);
        }
      }
    } finally {
      this.busy.set(false);
      await this.load(this.path());
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    void this.upload(event.dataTransfer?.files);
  }

  protected async download(entry: ProjectFileEntryDto): Promise<void> {
    try {
      const blob = await firstValueFrom(this.api.downloadProjectFile(this.projectId(), entry.path));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = entry.name;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      this.notifications.showError(`Failed to download ${entry.name}`, err);
    }
  }

  protected async remove(entry: ProjectFileEntryDto): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${entry.name}?`,
      message: entry.isDirectory ? 'The folder and everything in it is deleted from the server.' : 'The file is deleted from the server.',
      confirmText: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    try {
      await firstValueFrom(this.api.deleteProjectFile(this.projectId(), entry.path));
      if (this.file()?.path === entry.path || this.file()?.path.startsWith(entry.path + '/')) this.file.set(null);
      await this.load(this.path());
    } catch (err) {
      this.notifications.showError(`Failed to delete ${entry.name}`, err);
    }
  }

  /** What saving the open file does, for the git banner and the diff dialog. */
  protected saveNote(f: OpenFile): string {
    if (!this.listing()?.isGit) return this.isCompose() ? 'The compose file is validated before it is written.' : 'The file is written on the server.';
    return f.tracked
      ? 'This file is tracked in git: saving commits it and pushes it to the tracked branch, so the next sync keeps it.'
      : 'This file is not tracked in git: it is kept on the server but not versioned.';
  }

  protected async save(deploy: boolean): Promise<void> {
    const f = this.file();
    if (!f || this.busy()) return;
    const data: FileDiffData = { path: f.path, before: f.original, after: this.content(), deploy, note: this.saveNote(f) };
    const confirmed = await firstValueFrom(this.dialog.open(FileDiffDialog, { data, width: '900px', maxWidth: '95vw' }).afterClosed());
    if (!confirmed) return;

    this.busy.set(true);
    try {
      const content = this.content();
      const result = await firstValueFrom(this.api.saveProjectFile(this.projectId(), { path: f.path, content, deploy }));
      this.file.set({ ...f, original: content, tracked: result.tracked });
      this.notifications.success(
        result.commit ? `Saved and pushed ${result.commit.slice(0, 7)}` : result.tracked ? 'Saved' : this.listing()?.isGit ? 'Saved (not versioned in git)' : 'Saved'
      );
      if (result.deployment) this.deployed.emit(result.deployment);
      await this.load(this.path());
    } catch (err) {
      this.notifications.showError(`Failed to save ${f.path}`, err);
    } finally {
      this.busy.set(false);
    }
  }

  protected formatSize(entry: ProjectFileEntryDto): string {
    if (entry.isDirectory) return '';
    // Number(): integer schemas may be generated as a wrapper type.
    const bytes = Number(entry.size);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}
