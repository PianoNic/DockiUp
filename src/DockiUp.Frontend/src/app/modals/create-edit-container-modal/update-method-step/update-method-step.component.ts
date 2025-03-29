import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';

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
export class UpdateMethodStepComponent {
  @Input() updateMethodFormGroup!: FormGroup;

  constructor(){
    this.updateMethodFormGroup = new FormGroup({
      updateMethod: new FormControl('webhook', [Validators.required]),
      checkInterval: new FormControl({ value: 5, disabled: true }, [
        Validators.required,
        Validators.min(5),
        Validators.pattern('^[0-9]*$')
      ])
    });
  }

  onUpdateMethodChange(event: any): void {
    const value = event.value;
    if (value === 'interval') {
      this.updateMethodFormGroup.get('checkInterval')?.enable();
    } else {
      this.updateMethodFormGroup.get('checkInterval')?.disable();
    }
  }
}