import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { LocalDatePipe } from '../shared/pipes/local-date.pipe';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map } from 'rxjs';
import { ActivityService, AppService } from '../api';
import { Theme, ThemeService } from '../shared/services/theme.service';

/** Who you are (from the IdP token), your preferences, and what you've done lately. */
@Component({
  selector: 'app-user',
  imports: [LocalDatePipe, RouterLink, MatButtonModule, MatButtonToggleModule, MatIconModule],
  templateUrl: './user.html',
  styleUrl: './user.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class User {
  private readonly oidc = inject(OidcSecurityService);
  protected readonly theme = inject(ThemeService);

  protected readonly app = toSignal(inject(AppService).getAppInfo());
  protected readonly authEnabled = computed(() => this.app()?.authEnabled ?? false);

  private readonly claims = computed<Record<string, unknown>>(() => this.oidc.userData()?.userData ?? {});
  protected readonly name = computed(() =>
    this.str('name') ?? this.str('preferred_username') ?? this.str('email') ?? (this.authEnabled() ? 'Signed in' : 'Local user'),
  );
  protected readonly email = computed(() => this.str('email'));
  protected readonly username = computed(() => this.str('preferred_username'));
  protected readonly subject = computed(() => this.str('sub'));
  // Standard OIDC `picture` claim; initials are the fallback when absent or the image fails to load.
  protected readonly picture = computed(() => this.str('picture'));
  protected readonly pictureFailed = signal(false);
  // Keycloak publishes `roles`; Pocket ID, Authentik and Entra publish `groups`.
  protected readonly roles = computed(() => {
    const c = this.claims();
    return [c['roles'], c['groups']].flat().filter((r): r is string => typeof r === 'string');
  });
  protected readonly initials = computed(() =>
    this.name().split(/[\s._@-]+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join(''),
  );

  // The activity feed records the token's display name, so match on that. Anonymous actions log as system.
  private readonly activity = toSignal(inject(ActivityService).apiActivityGet(200 as never).pipe(map((e) => e ?? [])), {
    initialValue: [],
  });
  protected readonly mine = computed(() =>
    this.authEnabled() ? this.activity().filter((e) => e.actorName === this.name()).slice(0, 8) : [],
  );

  protected readonly themes: { value: Theme; label: string; icon: string }[] = [
    { value: 'light', label: 'Light', icon: 'light_mode' },
    { value: 'dark', label: 'Dark', icon: 'dark_mode' },
    { value: 'auto', label: 'Auto', icon: 'computer' },
  ];

  protected signOut(): void {
    this.oidc.logoffAndRevokeTokens().subscribe();
  }

  private str(key: string): string | undefined {
    const v = this.claims()[key];
    return typeof v === 'string' && v ? v : undefined;
  }
}
