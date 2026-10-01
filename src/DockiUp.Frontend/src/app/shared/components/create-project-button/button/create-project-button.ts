import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { CreateProjectModal, CreateProjectResult } from '../create-project-modal/create-project-modal';
import { ProjectService } from '../../../../api';
import { firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProjectStore } from '../../../stores/project.store';
@Component({
  selector: 'app-create-project-button',
  imports: [
    MatTooltipModule,
    CommonModule,
    MatButtonModule,
    MatMenuModule,
    MatIconModule
  ],
  templateUrl: './create-project-button.html',
  styleUrl: './create-project-button.scss'
})
export class CreateProjectButton {
  projectService = inject(ProjectService)
  projectStore = inject(ProjectStore);
  destroyRef = inject(DestroyRef);

  private readonly router = inject(Router);

  constructor(
    private dialog: MatDialog
  ) { }

  async openCreateDialog() {
    const dialogRef = this.dialog.open(CreateProjectModal, { width: '920px', maxWidth: '95vw' });

    // The dialog creates (or adopts) the project itself; open it right away: its first deployment streams there.
    dialogRef.afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(async (result: CreateProjectResult | undefined) => {
      if (result) await this.router.navigate(['/project', result.dockerProjectName]);
    });
  }
}
