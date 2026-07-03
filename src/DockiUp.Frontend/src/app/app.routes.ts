import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout';
import { Dashboard } from './dashboard/dashboard';
import { Containers } from './containers/containers';
import { Detail } from './detail/detail';
import { User } from './user/user';
import { Activity } from './activity/activity';
import { ContainerTerminal } from './container-terminal/container-terminal';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  {
    path: '',
    component: LayoutComponent,
    children: [
      { path: 'dashboard', component: Dashboard },
      { path: 'containers', component: Containers },
      { path: 'activity', component: Activity },
      { path: 'project/:id', component: Detail },
      { path: 'terminal/:containerId', component: ContainerTerminal },
      { path: 'me', component: User },
    ],
  },
  { path: '**', redirectTo: '/dashboard' },
];
