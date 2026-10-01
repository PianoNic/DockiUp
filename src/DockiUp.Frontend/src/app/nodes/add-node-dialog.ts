import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { NodeDto, NodesService } from '../api';
import { errorMessage } from '../shared/services/notification.service';

/** Mints a token + ready-to-deploy node compose (KRINT-style). Nothing is saved until "Add node". */
@Component({
  selector: 'app-add-node-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .compose-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin: 4px 0 8px; }
    .compose-head span { font-weight: 500; }
    pre {
      margin: 0; max-height: 300px; overflow: auto; padding: 16px 20px; border-radius: 24px;
      background: #1d2433; color: #dbe2f9;
      font: 12.5px/1.5 var(--x-mono);
    }
    .error { color: var(--mat-sys-error); font-size: 14px; }
    mat-form-field { width: 100%; }
  `,
  template: `
    <h2 mat-dialog-title>Add a node</h2>
    <mat-dialog-content>
      <p>Deploy this compose on the host you want to add. Nothing is saved until you press <strong>Add node</strong>.</p>

      <mat-form-field appearance="fill">
        <mat-label>Node name (optional)</mat-label>
        <input matInput [value]="name()" (input)="name.set($any($event.target).value)" placeholder="node-1" />
      </mat-form-field>

      @if (ready()) {
        <div class="compose-head">
          <span>Compose</span>
          <div>
            <button mat-button (click)="loadDraft()" [disabled]="loading()"><mat-icon>refresh</mat-icon>New token</button>
            <button mat-button (click)="copy()"><mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>{{ copied() ? 'Copied' : 'Copy' }}</button>
          </div>
        </div>
        <pre class="x-code">{{ compose() }}</pre>
      }

      @if (error(); as err) {
        <p class="error">{{ err }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)" [disabled]="saving()">Cancel</button>
      <button mat-flat-button (click)="save()" [disabled]="saving() || loading() || !ready()">
        {{ saving() ? 'Adding…' : 'Add node' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class AddNodeDialog {
  protected readonly ref = inject<MatDialogRef<AddNodeDialog, NodeDto | null>>(MatDialogRef);
  private readonly api = inject(NodesService);

  protected readonly name = signal('');
  private readonly token = signal('');
  private readonly serverUrl = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly copied = signal(false);
  // False until a draft loaded; the draft 400s when the control plane has no PUBLIC_URL.
  protected readonly ready = signal(false);

  // Same image as the server, booted in the node role by Node__ServerUrl (the DockiUp server it connects to). No Node__Id:
  // the control plane derives the node's identity from its token.
  protected readonly compose = computed(() =>
    [
      'services:',
      '  dockiup-node:',
      '    # Same image as the server: docker build -f src/DockiUp.API/Dockerfile -t dockiup:latest .',
      '    image: dockiup:latest',
      '    container_name: dockiup-node',
      '    restart: unless-stopped',
      '    environment:',
      `      Node__ServerUrl: "${this.serverUrl()}"   # the DockiUp server this node connects to`,
      `      Node__Token: "${this.token()}"`,
      `      Node__Name: "${this.name().trim() || 'node'}"`,
      '      SystemPaths__ProjectsPath: /app/projects',
      '    volumes:',
      '      # The node drives this host\'s Docker daemon and keeps its project checkouts here.',
      '      - /var/run/docker.sock:/var/run/docker.sock',
      '      - dockiup-node-projects:/app/projects',
      '',
      'volumes:',
      '  dockiup-node-projects:',
      '',
    ].join('\n'),
  );

  constructor() {
    this.loadDraft();
  }

  protected loadDraft(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiNodesDraftGet().subscribe({
      next: (draft) => {
        if (!this.name()) this.name.set(draft.suggestedName);
        this.token.set(draft.token);
        this.serverUrl.set(draft.serverUrl);
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err) => {
        this.ready.set(false);
        this.error.set(errorMessage(err));
        this.loading.set(false);
      },
    });
  }

  protected copy(): void {
    void navigator.clipboard?.writeText(this.compose()).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.api.apiNodesPost({ name: this.name().trim(), token: this.token() }).subscribe({
      next: (created) => this.ref.close(created),
      error: (err) => {
        this.error.set(errorMessage(err));
        this.saving.set(false);
      },
    });
  }
}
