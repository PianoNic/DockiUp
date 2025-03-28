import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import {MatButtonModule} from '@angular/material/button';
import { HeaderComponent } from "./header/header.component";

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    MatButtonModule,
    HeaderComponent
],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  title = 'DockiUp.Frontend';
}
