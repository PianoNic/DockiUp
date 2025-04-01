import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { CommonModule } from '@angular/common';
import { CreateEditContainerModalComponent } from '../../modals/create-edit-container-modal/create-edit-container-modal.component';
import { ContainerDto, CreateContainerDto } from '../../api';
import { ContainerStore } from '../../pages/dashboard/container.store';

@Component({
  selector: 'add-container-button',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatMenuModule,
    MatIconModule
  ],
  templateUrl: './add-container-button.component.html',
  styleUrl: './add-container-button.component.scss'
})
export class AddContainerButtonComponent {
  containerStore = inject(ContainerStore)

  constructor(
    private dialog: MatDialog
  ) {}

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateEditContainerModalComponent, { 
      data: { inEditMode: false }
    });

    dialogRef.afterClosed().subscribe((result: CreateContainerDto) => {
      if (result) {
        console.log('Container checkIntervals:', result.checkIntervals);
        console.log('Container description:', result.description);
        console.log('Container gitUrl:', result.gitUrl);
        console.log('Container name:', result.name);
        console.log('Container updateMethod:', result.updateMethod);

        this.containerStore.createContainer(result);
      }
    });
  }

  openImportDialog(): void {
   
  }
}