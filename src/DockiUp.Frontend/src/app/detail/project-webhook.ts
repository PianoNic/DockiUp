import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ProjectService, WebhookInfoDto } from '../api';
import { NotificationService } from '../shared/services/notification.service';

/** Where to point a git provider's push webhook for this project, and its secret. */
@Component({
  selector: 'app-project-webhook',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './project-deployments.scss',
  template: `
    @if (webhook(); as w) {
      <section class="panel x-card">
        <h2 class="section-title"><span class="x-shape tonal sm"><mat-icon>webhook</mat-icon></span> Push webhook</h2>
        <p class="hint">
          Add this as a push webhook in GitHub, Gitea/Forgejo or GitLab. Either content type works; use the secret
          below as the webhook secret (GitLab: "Secret token"). Any other tool can send it as an
          <code>X-Webhook-Secret</code> header.
          @if (w.branch) {
            Pushes to branches other than <code>{{ w.branch }}</code> are ignored.
          }
        </p>
        <div class="field">
          <span class="label">URL</span>
          <code class="value">{{ w.url }}</code>
          <button mat-icon-button (click)="copy(w.url, 'Webhook URL')" matTooltip="Copy URL" aria-label="Copy URL">
            <mat-icon>content_copy</mat-icon>
          </button>
        </div>
        <div class="field">
          <span class="label">Secret</span>
          <code class="value">{{ showSecret() ? w.secret : '••••••••••••••••••••••••' }}</code>
          <button mat-icon-button (click)="showSecret.set(!showSecret())" [matTooltip]="showSecret() ? 'Hide' : 'Show'" aria-label="Show or hide secret">
            <mat-icon>{{ showSecret() ? 'visibility_off' : 'visibility' }}</mat-icon>
          </button>
          <button mat-icon-button (click)="copy(w.secret, 'Webhook secret')" matTooltip="Copy secret" aria-label="Copy secret">
            <mat-icon>content_copy</mat-icon>
          </button>
        </div>
      </section>
    }
  `,
})
export class ProjectWebhook {
  readonly projectId = input.required<string>();

  private readonly api = inject(ProjectService);
  private readonly notifications = inject(NotificationService);

  protected readonly webhook = signal<WebhookInfoDto | null>(null);
  protected readonly showSecret = signal(false);

  constructor() {
    effect(() => {
      const id = this.projectId();
      firstValueFrom(this.api.getWebhook(id))
        .then((w) => this.webhook.set(w))
        .catch((err) => this.notifications.showError('Failed to load webhook details', err));
    });
  }

  protected copy(text: string, what: string): void {
    void navigator.clipboard?.writeText(text).then(() => this.notifications.success(`${what} copied`));
  }
}
