import { AfterViewInit, Component, Inject, OnInit, ViewChild } from '@angular/core';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogActions, MatDialogClose, MatDialogModule } from '@angular/material/dialog';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { BasicInfoStepComponent } from './basic-info-step/basic-info-step.component';
import { SummaryStepComponent } from './summary-step/summary-step.component';
import { UpdateMethodStepComponent } from './update-method-step/update-method-step.component';

interface DialogData {
  inEditMode: boolean;
  containerData?: any;
}

@Component({
  selector: 'app-create-edit-container-modal',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogActions,
    MatDialogClose,
    MatDialogModule,
    MatStepperModule,
    SummaryStepComponent,
    BasicInfoStepComponent,
    UpdateMethodStepComponent,
    MatButtonModule
  ],
  templateUrl: './create-edit-container-modal.component.html',
  styleUrl: './create-edit-container-modal.component.scss'
})
export class CreateEditContainerModalComponent implements OnInit, AfterViewInit {
  @ViewChild('stepper') stepper!: MatStepper;
  inEditMode: boolean;
  basicInfoFormGroup: FormGroup;
  updateMethodFormGroup: FormGroup;
  formData: any = {};

  constructor(
    private dialogRef: MatDialogRef<CreateEditContainerModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DialogData
  ) {
    this.inEditMode = data.inEditMode;

    this.basicInfoFormGroup = new FormGroup({
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
      description: new FormControl('', [Validators.maxLength(100)])
    });

    this.updateMethodFormGroup = new FormGroup({
      updateMethod: new FormControl('webhook', [Validators.required]),
      checkInterval: new FormControl({ value: 5, disabled: true }, [
        Validators.required,
        Validators.min(5),
        Validators.pattern('^[0-9]*$')
      ])
    });
  }

  ngOnInit(): void {
    if (this.inEditMode && this.data.containerData) {
      this.basicInfoFormGroup.patchValue(this.data.containerData);
      this.updateMethodFormGroup.patchValue(this.data.containerData);

      if (this.data.containerData.updateMethod === 'interval') {
        this.updateMethodFormGroup.get('checkInterval')?.enable();
      }
    }

    this.updateMethodFormGroup.get('updateMethod')?.valueChanges.subscribe(value => {
      if (value === 'interval') {
        this.updateMethodFormGroup.get('checkInterval')?.enable();
      } else {
        this.updateMethodFormGroup.get('checkInterval')?.disable();
      }
    });
  }

  isCurrentStepValid(): boolean {
    if (!this.stepper) 
      return false;
  
    switch (this.stepper.selectedIndex) {
      case 0:
        return this.basicInfoFormGroup.valid;
      case 1:
        return this.updateMethodFormGroup.valid;
      default:
        return true;
    }
  }  

  isFormValid(): boolean {
    return this.basicInfoFormGroup.valid && this.updateMethodFormGroup.valid;
  }

  onSubmit(): void {
    if (this.isFormValid()) {
      this.formData = { ...this.basicInfoFormGroup.value, ...this.updateMethodFormGroup.value };
      if (this.formData.updateMethod !== 'interval') {
        delete this.formData.checkInterval;
      }
      this.dialogRef.close(this.formData);
    }
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.stepper.selectionChange.subscribe(() => {
        this.isCurrentStepValid();
      });
    });
  }
  
}