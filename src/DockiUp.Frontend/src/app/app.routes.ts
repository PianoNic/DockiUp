import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout';
import { authGuard } from './shared/auth/auth.guard';

// Pages are lazy-loaded so the initial bundle only carries the shell (the terminal pulls in xterm).
export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  {
    path: '',
    component: LayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./dashboard/dashboard').then((m) => m.Dashboard) },
      { path: 'containers', loadComponent: () => import('./containers/containers').then((m) => m.Containers) },
      { path: 'activity', loadComponent: () => import('./activity/activity').then((m) => m.Activity) },
      { path: 'nodes', loadComponent: () => import('./nodes/nodes').then((m) => m.Nodes) },
      { path: 'project/:id', loadComponent: () => import('./detail/detail').then((m) => m.Detail) },
      { path: 'terminal/:containerId', loadComponent: () => import('./container-terminal/container-terminal').then((m) => m.ContainerTerminal) },
      { path: 'me', loadComponent: () => import('./user/user').then((m) => m.User) },
    ],
  },
  { path: '**', redirectTo: '/dashboard' },
];
