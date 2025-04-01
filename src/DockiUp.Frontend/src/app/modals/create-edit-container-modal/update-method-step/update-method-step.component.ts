import { CommonModule } from '@angular/common';
import { Component, Input, OnInit } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { UpdateMethod } from '../../../api';

@Component({
  selector: 'app-update-method-step',
  imports: [
    CommonModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    ReactiveFormsModule
  ],
  templateUrl: './update-method-step.component.html',
  styleUrl: './update-method-step.component.scss'
})
export class UpdateMethodStepComponent implements OnInit {
  @Input() updateMethodFormGroup!: FormGroup;

  // Expose UpdateMethod enum to template
  updateMethodOptions = UpdateMethod;

  ngOnInit(): void {
    if (!this.updateMethodFormGroup) {
      this.initForm();
    }
  }

  initForm(): void {
    this.updateMethodFormGroup = new FormGroup({
      updateMethod: new FormControl(UpdateMethod.UseWebhook, [Validators.required]),
      checkInterval: new FormControl({ value: 5, disabled: true }, [
        Validators.required,
        Validators.min(5),
        Validators.pattern('^[0-9]*$')
      ])
    });
  }

  onUpdateMethodChange(event: any): void {
    const value = event.value;
    if (value === UpdateMethod.CheckPeriodically) {
      this.updateMethodFormGroup.get('checkInterval')?.enable();
    } else {
      this.updateMethodFormGroup.get('checkInterval')?.disable();
    }
  }

  // Helper method to get direct access to form values
  getFormValues() {
    const formValue = this.updateMethodFormGroup.getRawValue();
    return {
      updateMethod: formValue.updateMethod as UpdateMethod,
      checkInterval: formValue.updateMethod === UpdateMethod.CheckPeriodically ?
        formValue.checkInterval : undefined
    };
  }
}