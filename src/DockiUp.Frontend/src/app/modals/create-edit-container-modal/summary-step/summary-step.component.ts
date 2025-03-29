import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormGroup } from '@angular/forms';

@Component({
  selector: 'app-summary-step',
  imports: [
    CommonModule
  ],
  templateUrl: './summary-step.component.html',
  styleUrl: './summary-step.component.scss'
})
export class SummaryStepComponent {
  @Input() basicInfoFormGroup!: FormGroup;
  @Input() updateMethodFormGroup!: FormGroup;
}