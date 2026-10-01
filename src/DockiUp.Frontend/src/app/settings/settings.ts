import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { NotificationChannels } from './notification-channels';
import { GitCredentials } from './git-credentials';
import { VaultSecrets } from './vault-secrets';

/** App-wide settings: where notifications go, git credentials for private repos, and the secrets vault. */
@Component({
  selector: 'app-settings',
  imports: [MatIconModule, MatTabsModule, NotificationChannels, GitCredentials, VaultSecrets],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings.scss',
  template: `
    <div class="x-page">
      <header class="x-page-header">
        <div>
          <span class="x-eyebrow">Configuration</span>
          <h1 class="x-title">Settings</h1>
          <p class="x-sub">Notifications, git credentials and the secrets vault</p>
        </div>
      </header>
      <!-- Tabs instead of stacked sections, so the page itself never scrolls. -->
      <mat-tab-group class="settings-tabs" animationDuration="0ms" mat-stretch-tabs="false" preserveContent
        [selectedIndex]="tab()" (selectedIndexChange)="tab.set($event)">
        <mat-tab>
          <ng-template mat-tab-label><span class="tab-label"><mat-icon>notifications</mat-icon>Notifications</span></ng-template>
          <div class="tab-fill"><app-notification-channels /></div>
        </mat-tab>
        <mat-tab>
          <ng-template mat-tab-label><span class="tab-label"><mat-icon>key</mat-icon>Git credentials</span></ng-template>
          <div class="tab-fill"><app-git-credentials /></div>
        </mat-tab>
        <mat-tab>
          <ng-template mat-tab-label><span class="tab-label"><mat-icon>lock</mat-icon>Secrets vault</span></ng-template>
          <div class="tab-fill"><app-vault-secrets /></div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
})
export class Settings {
  protected readonly tab = signal(0);
}
