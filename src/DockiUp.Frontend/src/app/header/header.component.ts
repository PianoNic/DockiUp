import { Component, OnDestroy, OnInit, Renderer2 } from '@angular/core';
import {MatIconModule} from '@angular/material/icon';
import {MatButtonModule} from '@angular/material/button';
import {MatToolbarModule} from '@angular/material/toolbar';

@Component({
  selector: 'app-header',
  imports: [
    MatToolbarModule, 
    MatButtonModule, 
    MatIconModule
  ],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent implements OnInit, OnDestroy {
  private themeOptions: Array<'system' | 'light' | 'dark'> = ['system', 'light', 'dark'];
  private currentThemeIndex = 0;
  private mediaQueryList: MediaQueryList;
  private mediaQueryHandler: (e: MediaQueryListEvent) => void;

  constructor(private renderer: Renderer2) {
    this.mediaQueryList = window.matchMedia('(prefers-color-scheme: dark)');
    
    this.mediaQueryHandler = (e: MediaQueryListEvent) => {
      if (this.currentTheme === 'system') {
        if (e.matches) {
          this.renderer.addClass(document.documentElement, 'dark');
          this.renderer.removeClass(document.documentElement, 'light');
        } else {
          this.renderer.addClass(document.documentElement, 'light');
          this.renderer.removeClass(document.documentElement, 'dark');
        }
      }
    };
  }

  get currentTheme(): 'system' | 'light' | 'dark' {
    return this.themeOptions[this.currentThemeIndex];
  }

  get currentThemeIcon(): string {
    switch (this.currentTheme) {
      case 'system': return 'computer';
      case 'light': return 'light_mode';
      case 'dark': return 'dark_mode';
      default: return 'light_mode';
    }
  }

  ngOnInit() {
    this.initializeTheme();
    this.mediaQueryList.addEventListener('change', this.mediaQueryHandler);
  }

  ngOnDestroy() {
    this.mediaQueryList.removeEventListener('change', this.mediaQueryHandler);
  }

  cycleTheme() {
    this.currentThemeIndex = (this.currentThemeIndex + 1) % this.themeOptions.length;
    this.applyTheme(this.currentTheme);
  }

  private initializeTheme() {
    const savedTheme = localStorage.getItem('theme');
    if (savedTheme && ['system', 'light', 'dark'].includes(savedTheme)) {
      this.currentThemeIndex = this.themeOptions.indexOf(savedTheme as 'system' | 'light' | 'dark');
      this.applyTheme(this.currentTheme);
    } else {
      this.applyTheme('system');
    }
  }

  private applyTheme(theme: 'system' | 'light' | 'dark') {
    // Save theme preference
    localStorage.setItem('theme', theme);

    // Remove existing theme classes
    this.renderer.removeClass(document.documentElement, 'dark');
    this.renderer.removeClass(document.documentElement, 'light');

    switch (theme) {
      case 'system':
        this.applySystemTheme();
        break;
      case 'light':
        this.renderer.addClass(document.documentElement, 'light');
        break;
      case 'dark':
        this.renderer.addClass(document.documentElement, 'dark');
        break;
    }
  }

  private applySystemTheme() {
    // Check system preference
    const prefersDarkScheme = window.matchMedia('(prefers-color-scheme: dark)');
    
    if (prefersDarkScheme.matches) {
      this.renderer.addClass(document.documentElement, 'dark');
    } else {
      this.renderer.addClass(document.documentElement, 'light');
    }
  }
}