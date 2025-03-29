import { Component, OnInit } from '@angular/core';
import { Observable } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { UpdateStatusType } from '../../enums/update-status-type.enum';
import { Container } from '../../models/container.model';
import { ContainerService } from '../../services/container.service';
import { AddContainerButtonComponent } from "../../components/add-container-button/add-container-button.component";

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
  containers$: Observable<Container[]> | undefined;
  statusCounts = {
    total: 0,
    updated: 0,
    needsUpdate: 0,
    updating: 0,
    failed: 0
  };

  constructor(private containerService: ContainerService) { }

  ngOnInit(): void {
    this.loadContainers();
  }

  loadContainers(): void {
    this.containers$ = this.containerService.getContainers();
    // TODO: Calculate status counts
  }

  updateAllContainers(): void {
    // TODO: Implement method to update all containers
  }

  checkAllForUpdates(): void {
    // TODO: Implement method to check all containers for updates
  }

  getStatusClass(status: UpdateStatusType): string {
    // TODO: Return appropriate CSS class based on status
    return '';
  }
}