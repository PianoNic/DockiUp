import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import {MatButtonModule} from '@angular/material/button';
import { FooterComponent } from "./static-elements/footer/footer.component";
import { HeaderComponent } from "./static-elements/header/header.component";
import { AppService } from './api';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    MatButtonModule,
    FooterComponent,
    HeaderComponent
],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  constructor(private appService: AppService){
    this.appService.getAppInfo()
    .pipe(takeUntilDestroyed())
    .subscribe(a => console.log(a));
  }
}
