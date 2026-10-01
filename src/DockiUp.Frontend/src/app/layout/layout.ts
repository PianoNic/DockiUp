import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CreateProjectButton } from '../shared/components/create-project-button/button/create-project-button';
import { DockiUpHubService } from '../shared/services/dockiup-hub.service';
import { Layout as LayoutService } from '../shared/services/layout';
import { Header } from './header/header';
import { Sidenav } from './sidenav/sidenav';

@Component({
  selector: 'app-layout',
  imports: [RouterOutlet, RouterLink, MatButtonModule, MatIconModule, CreateProjectButton, Header, Sidenav],
  templateUrl: './layout.html',
  styleUrl: './layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LayoutComponent {
  readonly layoutService = inject(LayoutService);

  constructor() {
    inject(DockiUpHubService); // start SignalR connection for live updates
  }
}
