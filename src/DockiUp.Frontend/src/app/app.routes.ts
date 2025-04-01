import { Routes } from '@angular/router';
import { ContainerDetailComponent } from './pages/container-detail/container-detail.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'dashboard', component: DashboardComponent },
  { path: 'container/:id', component: ContainerDetailComponent },
  // { path: 'add-container', component: ContainerFormComponent },
  // { path: 'edit-container/:id', component: ContainerFormComponent },
  // { path: 'settings', component: SettingsComponent },
  { path: '**', redirectTo: '/dashboard' }
];
