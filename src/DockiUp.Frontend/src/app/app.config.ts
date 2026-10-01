import { ApplicationConfig, importProvidersFrom, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { authInterceptor, provideAuth, withAppInitializerAuthCheck } from 'angular-auth-oidc-client';
import { environment } from '../environments/environment';
import { ApiModule, Configuration } from './api';
import { MatIconRegistry } from '@angular/material/icon';
import { ThemeService } from './shared/services/theme.service';
import { authLoaderProvider } from './shared/auth/auth.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAnimations(),
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    // authInterceptor attaches the bearer token to the API's secureRoutes; harmless in open mode
    // where no token exists.
    provideHttpClient(withInterceptors([authInterceptor()])),
    importProvidersFrom(
      ApiModule.forRoot(() => new Configuration({
        basePath: environment.apiBaseUrl,
      })),
    ),
    // OIDC is configured from GetAppInfo at runtime; withAppInitializerAuthCheck settles the session
    // before the first guarded route resolves.
    provideAuth({ loader: authLoaderProvider }, withAppInitializerAuthCheck()),
    provideAppInitializer(() => {
      const themeService = inject(ThemeService);
      themeService.setTheme(themeService.getTheme()());
    }),
    // Every <mat-icon> uses Material Symbols Rounded (ligatures, so the icon names stay the same).
    provideAppInitializer(() => { inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-rounded', 'mat-ligature-font'); }),
  ]
};
