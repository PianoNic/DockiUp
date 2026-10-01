import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { NotificationChannels } from './notification-channels';
import { GitCredentials } from './git-credentials';
import { VaultSecrets } from './vault-secrets';

/** App-wide settings: where notifications go, git credentials for private repos, and the secrets vault. */
@Component({
  selector: 'app-settings',
  imports: [MatTabsModule, NotificationChannels, GitCredentials, VaultSecrets],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../activity/activity.scss', './settings.scss'],
  template: `
    <div class="activity-wrapper">
      <header class="activity-header">
        <div>
          <h1 class="activity-title">Settings</h1>
          <p class="activity-subtitle">Notifications, git credentials and the secrets vault</p>
        </div>
      </header>
      <!-- Tabs instead of stacked sections, so the page itself never scrolls. -->
      <mat-tab-group class="settings-tabs" animationDuration="0ms" mat-stretch-tabs="false" preserveContent
        [selectedIndex]="tab()" (selectedIndexChange)="tab.set($event)">
        <mat-tab label="Notifications"><div class="tab-fill"><app-notification-channels /></div></mat-tab>
        <mat-tab label="Git credentials"><div class="tab-fill"><app-git-credentials /></div></mat-tab>
        <mat-tab label="Secrets"><div class="tab-fill"><app-vault-secrets /></div></mat-tab>
      </mat-tab-group>
    </div>
  `,
})
export class Settings {
  protected readonly tab = signal(0);
}
