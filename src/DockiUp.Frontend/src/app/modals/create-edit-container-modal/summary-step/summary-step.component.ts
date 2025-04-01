import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { UpdateMethod } from '../../../api';

@Component({
  selector: 'app-summary-step',
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatListModule
  ],
  templateUrl: './summary-step.component.html',
  styleUrl: './summary-step.component.scss'
})
export class SummaryStepComponent {
  @Input() basicInfoFormGroup!: FormGroup;
  @Input() updateMethodFormGroup!: FormGroup;
  
  updateMethodEnum = UpdateMethod;
  
  updateMethodNames = {
    [UpdateMethod.UseWebhook]: 'Use Webhook',
    [UpdateMethod.UpdateManually]: 'Update Manually',
    [UpdateMethod.CheckPeriodically]: 'Check Periodically'
  };
  
  get name(): string {
    return this.basicInfoFormGroup?.get('name')?.value || '';
  }
  
  get gitUrl(): string {
    return this.basicInfoFormGroup?.get('gitUrl')?.value || '';
  }
  
  get description(): string {
    return this.basicInfoFormGroup?.get('description')?.value || '';
  }
  
  get updateMethod(): UpdateMethod {
    return this.updateMethodFormGroup?.get('updateMethod')?.value || UpdateMethod.UseWebhook;
  }
  
  get updateMethodDisplay(): string {
    return this.updateMethodNames[this.updateMethod] || 'Unknown';
  }
  
  get checkInterval(): number | null {
    if (this.updateMethod === UpdateMethod.CheckPeriodically) {
      return this.updateMethodFormGroup?.get('checkInterval')?.value || null;
    }
    return null;
  }
}