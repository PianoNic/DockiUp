import { Component, OnInit, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { AddContainerButtonComponent } from "../../components/add-container-button/add-container-button.component";
import { ContainerStore } from './container.store';
import { StatusType } from '../../api';

interface Container {
  id: string;
  name: string;
  description: string;
  lastUpdated: Date;
  updateStatus: {
    status: 'updated' | 'needs_update' | 'updating' | 'failed' | 'unknown';
  };
}

@Component({
  selector: 'app-dashboard',
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    RouterLink,
    MatButtonModule,
    AddContainerButtonComponent
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  StatusType = StatusType;
  public containerStore = inject(ContainerStore);

  containers = this.containerStore.containers;
  loading = this.containerStore.loading;
  error = this.containerStore.error;

  ngOnInit(): void {
    this.containerStore.loadContainers();
  }

  getStatusIcon(status: StatusType): string {
    switch (status) {
      case StatusType.Stopped: return 'pause';
      case StatusType.Running: return 'check_circle';
      case StatusType.NeedsUpdate: return 'new_releases';
      case StatusType.Updating: return 'loop';
      case StatusType.Failed: return 'error';
      default: return '';
    }
  }
}
