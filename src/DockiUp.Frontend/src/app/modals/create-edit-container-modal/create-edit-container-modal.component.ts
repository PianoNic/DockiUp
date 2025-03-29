import { Component, Inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MatDialogActions,
  MatDialogClose,
  MatDialogContent,
  MatDialogTitle,
  MatDialogRef,
  MAT_DIALOG_DATA
} from '@angular/material/dialog';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';

interface DialogData {
  inEditMode: boolean;
  containerData?: any;
}

@Component({
  selector: 'app-create-edit-container-modal',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogActions,
    MatDialogClose,
    MatDialogTitle,
    MatDialogContent,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule
  ],
  templateUrl: './create-edit-container-modal.component.html',
  styleUrl: './create-edit-container-modal.component.scss'
})
export class CreateEditContainerModalComponent implements OnInit {
  inEditMode: boolean;
  createEditFormGroup: FormGroup;

  constructor(
    private dialogRef: MatDialogRef<CreateEditContainerModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DialogData
  ) {
    this.inEditMode = data.inEditMode;

    this.createEditFormGroup = new FormGroup({
      name: new FormControl('', [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(20),
        Validators.pattern('^(?!\\s*$).+')
      ]),
      gitUrl: new FormControl('', [
        Validators.required,
        Validators.pattern('^https?://.*'),
        Validators.maxLength(100),
        Validators.pattern('^(?!\\s*$).+')
      ]),
      description: new FormControl('', [
        Validators.maxLength(100)
      ])
    });
  }

  ngOnInit(): void {
    // Populate form if in edit mode
    if (this.inEditMode && this.data.containerData) {
      this.createEditFormGroup.patchValue({
        name: this.data.containerData.name,
        gitUrl: this.data.containerData.gitUrl,
        description: this.data.containerData.description
      });
    }
  }

  onSubmit(): void {
    if (this.createEditFormGroup.valid) {
      this.dialogRef.close(this.createEditFormGroup.value);
    }
  }

  onCancel(): void {
    this.dialogRef.close();
  }
}