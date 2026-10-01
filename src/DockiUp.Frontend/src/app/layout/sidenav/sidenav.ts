import { Component, ChangeDetectionStrategy, input, inject, OnInit, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, NavigationEnd, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { NAVIGATION_ITEMS } from '../../shared/models/navigation.model';
import { Layout } from '../../shared/services/layout';

@Component({
  selector: 'app-sidenav',
  imports: [MatIconModule, MatTooltipModule, RouterLink, RouterLinkActive],
  templateUrl: './sidenav.html',
  styleUrl: './sidenav.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Sidenav implements OnInit {
  private readonly layoutService = inject(Layout);
  private readonly router = inject(Router);

  collapsed = input(false);
  navigationItems = signal(NAVIGATION_ITEMS);

  ngOnInit(): void {
    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe(() => {
        if (this.layoutService.isMobile()) {
          this.layoutService.closeSidenav();
        }
      });
  }
}
