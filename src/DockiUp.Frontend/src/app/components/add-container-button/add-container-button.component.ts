import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'add-container-button',
  imports: [
    MatButtonModule,
    MatMenuModule,
    MatIconModule
  ],
  templateUrl: './add-container-button.component.html',
  styleUrl: './add-container-button.component.scss'
})
export class AddContainerButtonComponent {

}
