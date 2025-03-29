import { Routes } from '@angular/router';
import { ContainerDetailComponent } from './pages/container-detail/container-detail.component';
import { ContainerListComponent } from './pages/container-list/container-list.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'dashboard', component: DashboardComponent },
  { path: 'containers', component: ContainerListComponent },
  { path: 'containers/:id', component: ContainerDetailComponent },
  // { path: 'add-container', component: ContainerFormComponent },
  // { path: 'edit-container/:id', component: ContainerFormComponent },
  // { path: 'settings', component: SettingsComponent },
  { path: '**', redirectTo: '/dashboard' }
];
