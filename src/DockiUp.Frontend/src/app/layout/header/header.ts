import { Component, ChangeDetectionStrategy, output, signal, computed, inject, ElementRef, HostListener } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map } from 'rxjs/operators';
import { ThemeToggleComponent } from '../../shared/components/theme-toggle/theme-toggle.component';
import { CreateProjectButton } from '../../shared/components/create-project-button/button/create-project-button';
import { AppService } from '../../api';

@Component({
  selector: 'app-header',
  imports: [
    CommonModule,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    RouterLink,
    ThemeToggleComponent,
    CreateProjectButton,
  ],
  templateUrl: './header.html',
  styleUrl: './header.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Header {
  private readonly elementRef = inject(ElementRef);
  private readonly oidc = inject(OidcSecurityService);
  private readonly appService = inject(AppService);

  menuToggle = output<void>();
  userMenuOpen = signal(false);

  // Auth is optional. When the backend reports authEnabled=false the header stays in open mode:
  // a static user, no login/logout.
  protected readonly authEnabled = toSignal(
    this.appService.getAppInfo().pipe(map((app) => app.authEnabled ?? false)),
    { initialValue: false },
  );

  private readonly authState = toSignal(this.oidc.isAuthenticated$);
  protected readonly isAuthenticated = computed(() => this.authState()?.isAuthenticated ?? false);

  private readonly userData = this.oidc.userData;
  protected readonly displayName = computed(() => {
    const data = this.userData()?.userData;
    return data?.name ?? data?.preferred_username ?? data?.email ?? 'User';
  });
  protected readonly email = computed(() => this.userData()?.userData?.email ?? 'DockiUp');

  onMenuToggle(): void {
    this.menuToggle.emit();
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((open) => !open);
  }

  closeUserMenu(): void {
    this.userMenuOpen.set(false);
  }

  login(): void {
    this.oidc.authorize();
  }

  logout(): void {
    this.oidc.logoffAndRevokeTokens().subscribe();
    this.closeUserMenu();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.closeUserMenu();
    }
  }
}
