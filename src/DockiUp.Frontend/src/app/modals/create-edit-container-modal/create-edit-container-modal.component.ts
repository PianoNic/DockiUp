import { AfterViewInit, Component, Inject, OnInit, ViewChild } from '@angular/core';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogActions, MatDialogClose, MatDialogModule } from '@angular/material/dialog';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { BasicInfoStepComponent } from './basic-info-step/basic-info-step.component';
import { SummaryStepComponent } from './summary-step/summary-step.component';
import { UpdateMethodStepComponent } from './update-method-step/update-method-step.component';
import { UpdateMethod } from '../../api';

interface DialogData {
  inEditMode: boolean;
  containerData?: any;
}

interface ContainerFormData {
  name: string;
  gitUrl: string;
  description: string;
  updateMethod: UpdateMethod;
  checkInterval?: number;
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
  @ViewChild(UpdateMethodStepComponent) updateMethodStep!: UpdateMethodStepComponent;
  @ViewChild(BasicInfoStepComponent) basicInfoStep!: BasicInfoStepComponent;

  inEditMode: boolean;
  basicInfoFormGroup: FormGroup;
  updateMethodFormGroup: FormGroup;
  formData: ContainerFormData = {} as ContainerFormData;

  constructor(
    private dialogRef: MatDialogRef<CreateEditContainerModalComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DialogData
  ) {
    this.inEditMode = data.inEditMode;

    // Initialize form groups
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
      updateMethod: new FormControl(UpdateMethod.UseWebhook, [Validators.required]),
      checkInterval: new FormControl({ value: 5, disabled: true }, [
        Validators.required,
        Validators.min(5),
        Validators.pattern('^[0-9]*$')
      ])
    });
  }

  ngOnInit(): void {
    if (this.inEditMode && this.data.containerData) {
      this.patchFormValues(this.data.containerData);
    }
  }

  patchFormValues(containerData: any): void {
    // Patch basic info
    this.basicInfoFormGroup.patchValue({
      name: containerData.name || '',
      gitUrl: containerData.gitUrl || '',
      description: containerData.description || ''
    });

    // Map string values to enum values if needed
    let updateMethod = containerData.updateMethod;
    if (typeof updateMethod === 'string') {
      // Map from legacy string values to enum values
      switch (updateMethod) {
        case 'webhook':
          updateMethod = UpdateMethod.UseWebhook;
          break;
        case 'manually':
          updateMethod = UpdateMethod.UpdateManually;
          break;
        case 'interval':
          updateMethod = UpdateMethod.CheckPeriodically;
          break;
      }
    }

    // Patch update method
    this.updateMethodFormGroup.patchValue({
      updateMethod: updateMethod || UpdateMethod.UseWebhook,
      checkInterval: containerData.checkInterval || 5
    });

    // Enable interval field if needed
    if (updateMethod === UpdateMethod.CheckPeriodically) {
      this.updateMethodFormGroup.get('checkInterval')?.enable();
    } else {
      this.updateMethodFormGroup.get('checkInterval')?.disable();
    }
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
      // Combine form data
      const basicInfo = this.basicInfoFormGroup.value;

      // Use the helper method to get properly formatted update method data
      const updateMethodData = this.updateMethodStep?.getFormValues() || this.updateMethodFormGroup.getRawValue();

      // Merge the data
      this.formData = {
        ...basicInfo,
        ...updateMethodData
      };

      this.dialogRef.close(this.formData);
    }
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  ngAfterViewInit(): void {
    // Set up stepper validation
    setTimeout(() => {
      this.stepper.selectionChange.subscribe(() => {
        this.isCurrentStepValid();
      });
    });
  }
}