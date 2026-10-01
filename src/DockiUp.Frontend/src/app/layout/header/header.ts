import { Component, ChangeDetectionStrategy, input, signal, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map, shareReplay } from 'rxjs/operators';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AppService } from '../../api';
import { TaskCenter } from './task-center';

@Component({
  selector: 'app-header',
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    RouterLink,
    RouterLinkActive,
    MatTooltipModule,
    TaskCenter,
  ],
  templateUrl: './header.html',
  styleUrl: './header.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Header {
  private readonly oidc = inject(OidcSecurityService);
  private readonly appService = inject(AppService);

  /** Collapsed rail: avatar and bell only. */
  readonly collapsed = input(false);

  // Auth is optional. When the backend reports authEnabled=false the header stays in open mode:
  // a static user, no login/logout.
  private readonly appInfo$ = this.appService.getAppInfo().pipe(shareReplay(1));
  protected readonly authEnabled = toSignal(this.appInfo$.pipe(map((app) => app.authEnabled ?? false)), { initialValue: false });
  protected readonly version = toSignal(this.appInfo$.pipe(map((app) => app.version ?? '')), { initialValue: '' });

  private readonly authState = toSignal(this.oidc.isAuthenticated$);
  protected readonly isAuthenticated = computed(() => this.authState()?.isAuthenticated ?? false);

  private readonly userData = this.oidc.userData;
  protected readonly displayName = computed(() => {
    const data = this.userData()?.userData;
    return data?.name ?? data?.preferred_username ?? data?.email ?? 'User';
  });
  protected readonly email = computed(() => this.userData()?.userData?.email ?? 'DockiUp');
  protected readonly picture = computed<string | undefined>(() => this.userData()?.userData?.picture || undefined);
  protected readonly pictureFailed = signal(false);
  protected readonly initials = computed(() =>
    String(this.displayName()).split(/[\s@._-]+/).filter(Boolean).slice(0, 2).map((w: string) => w[0].toUpperCase()).join('') || 'U');

  login(): void {
    this.oidc.authorize();
  }
}
