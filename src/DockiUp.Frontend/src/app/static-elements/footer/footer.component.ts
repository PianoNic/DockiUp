import { Component } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faGithub } from '@fortawesome/free-brands-svg-icons';
import { AppService } from '../../api';
import { catchError, firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-footer',
  imports: [
    FontAwesomeModule
  ],
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.scss'
})
export class FooterComponent {
  currentYear: number = new Date().getFullYear();
  faGithub = faGithub;

  version = 'unknown';
  environment = 'unknown';

  constructor(private appService: AppService){
    this.initializeApp();
  }

  async initializeApp() {
    const appInfo = await firstValueFrom(
      this.appService.getAppInfo().pipe(
        takeUntilDestroyed()
      )
    );

    this.version = appInfo.version!;
    this.environment = appInfo.environment!;
  }
}
