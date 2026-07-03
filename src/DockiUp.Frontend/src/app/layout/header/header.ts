import { Component, ChangeDetectionStrategy, output, signal, inject, ElementRef, HostListener } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ThemeToggleComponent } from '../../shared/components/theme-toggle/theme-toggle.component';
import { CreateProjectButton } from '../../shared/components/create-project-button/button/create-project-button';

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

  menuToggle = output<void>();
  userMenuOpen = signal(false);

  onMenuToggle(): void {
    this.menuToggle.emit();
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((open) => !open);
  }

  closeUserMenu(): void {
    this.userMenuOpen.set(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.closeUserMenu();
    }
  }
}
