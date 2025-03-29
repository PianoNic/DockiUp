import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { CommonModule } from '@angular/common';
import { CreateEditContainerModalComponent } from '../../modals/create-edit-container-modal/create-edit-container-modal.component';

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
  constructor(private dialog: MatDialog) {}

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateEditContainerModalComponent, {
      width: '500px',
      data: { inEditMode: false }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        console.log('Container created:', result);
        // Handle the created container data here
        // For example, send it to a service to save it
      }
    });
  }

  openImportDialog(): void {
    // // You can either reuse the same dialog with different configuration
    // // or create a separate import dialog component
    // const dialogRef = this.dialog.open(CreateEditContainerModalComponent, {
    //   width: '500px',
    //   data: { inEditMode: false, isImport: true }
    // });

    // dialogRef.afterClosed().subscribe(result => {
    //   if (result) {
    //     console.log('Container imported:', result);
    //     // Handle the imported container data here
    //   }
    // });
  }
}