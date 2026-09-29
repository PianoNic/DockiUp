import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmText?: string;
  /** Styles the confirm button as destructive (error colour). */
  destructive?: boolean;
  /** Optional opt-in shown as a checkbox (unchecked by default); its value comes back from askWithOption. */
  option?: string;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatButtonModule, MatCheckboxModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `.destructive { --mat-button-filled-container-color: var(--mat-sys-error); --mat-button-filled-label-text-color: var(--mat-sys-on-error); }`,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      @if (data.option) {
        <mat-checkbox [checked]="checked()" (change)="checked.set($event.checked)">{{ data.option }}</mat-checkbox>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">Cancel</button>
      <button mat-flat-button [class.destructive]="data.destructive" [mat-dialog-close]="{ checked: checked() }" cdkFocusInitial>
        {{ data.confirmText ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmOptions>(MAT_DIALOG_DATA);
  protected readonly checked = signal(false);
}

/** Material replacement for window.confirm(): `if (await confirm.ask({...})) ...` */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);

  async ask(options: ConfirmOptions): Promise<boolean> {
    return (await this.askWithOption(options)) !== null;
  }

  /** null when cancelled, otherwise whether the optional checkbox was ticked. */
  async askWithOption(options: ConfirmOptions): Promise<{ checked: boolean } | null> {
    const ref = this.dialog.open<ConfirmDialog, ConfirmOptions, { checked: boolean } | null>(ConfirmDialog, {
      data: options,
      width: '440px',
      maxWidth: '95vw',
    });
    return (await firstValueFrom(ref.afterClosed())) ?? null;
  }
}
