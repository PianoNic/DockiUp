import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map, of, switchMap, take } from 'rxjs';
import { AppService } from '../../api';

// Auth is optional. In open mode (authEnabled=false) every route is public; when enabled an
// unauthenticated visitor is redirected to the IdP. Relies on withAppInitializerAuthCheck() having
// settled the session at startup, so isAuthenticated$ already reflects the real state here.
export const authGuard: CanActivateFn = () => {
  const appService = inject(AppService);
  const oidc = inject(OidcSecurityService);

  return appService.getAppInfo().pipe(
    take(1),
    switchMap((app) => {
      if (!app.authEnabled) return of(true);
      return oidc.isAuthenticated$.pipe(
        take(1),
        map(({ isAuthenticated }) => {
          if (isAuthenticated) return true;
          oidc.authorize();
          return false;
        }),
      );
    }),
  );
};
