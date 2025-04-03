import { AfterViewInit, Component, inject, Inject, OnInit, ViewChild } from '@angular/core';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogActions, MatDialogClose, MatDialogModule } from '@angular/material/dialog';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { BasicInfoStepComponent } from './basic-info-step/basic-info-step.component';
import { UpdateMethodStepComponent } from './update-method-step/update-method-step.component';
import { CreateContainerDto, UpdateMethod } from '../../api';
import { ContainerStore } from '../../pages/dashboard/container.store';
import { MatProgressBarModule } from '@angular/material/progress-bar';

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
    BasicInfoStepComponent,
    UpdateMethodStepComponent,
    MatButtonModule,
    MatProgressBarModule
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

  containerStore = inject(ContainerStore);

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
      this.dialogRef.close(this.getFormData());
    }
  }

  getFormData(): CreateContainerDto {
    if (this.isFormValid()) {
      // Combine form data
      const basicInfo = this.basicInfoFormGroup.value;
      const updateMethodData = this.updateMethodStep?.getFormValues() || this.updateMethodFormGroup.getRawValue();

      // Create proper CreateContainerDto object
      const containerDto: CreateContainerDto = {
        name: basicInfo.name,
        gitUrl: basicInfo.gitUrl,
        description: basicInfo.description,
        updateMethod: updateMethodData.updateMethod,
        checkIntervals: updateMethodData.updateMethod === UpdateMethod.CheckPeriodically ?
          updateMethodData.checkInterval : undefined
      };

      return containerDto;
    }
    throw new Error('Form is not valid');
  }

  async setupFiles(): Promise<void> {
    if (this.isFormValid()) {
      const result = this.getFormData();
      this.containerStore.createContainer(result);
      await this.waitForLoadingToComplete();
      this.stepper.next();
    }
  }
  
  private waitForLoadingToComplete(): Promise<void> {
    return new Promise((resolve) => {
      const interval = setInterval(() => {
        if (!this.containerStore.loading()) {
          clearInterval(interval);
          resolve();
        }
      }, 100); 
    });
  }

  get isLoading(): boolean {
    return this.containerStore.loading();
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