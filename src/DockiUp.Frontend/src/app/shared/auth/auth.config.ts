import { StsConfigHttpLoader, StsConfigLoader } from 'angular-auth-oidc-client';
import { map } from 'rxjs/operators';
import { AppService } from '../../api';
import { environment } from '../../../environments/environment';

// OIDC is configured at runtime from GetAppInfo so a single build works against any IdP (and, when
// authEnabled is false, against none). An empty authority leaves the client dormant: nothing redirects
// until authorize() is called, so open mode just works.
export const authLoaderFactory = (appService: AppService) => {
  const config$ = appService.getAppInfo().pipe(
    map((app) => ({
      authority: app.authority ?? '',
      redirectUrl: app.redirectUri ?? '',
      postLogoutRedirectUri: app.postLogoutRedirectUri ?? '',
      clientId: app.clientId ?? '',
      scope: app.scope ?? 'openid profile email',
      responseType: 'code',
      silentRenew: true,
      useRefreshToken: true,
      renewTimeBeforeTokenExpiresInSeconds: 30,
      secureRoutes: [environment.apiBaseUrl],
    })),
  );
  return new StsConfigHttpLoader(config$);
};

export const authLoaderProvider = {
  provide: StsConfigLoader,
  useFactory: authLoaderFactory,
  deps: [AppService],
};
