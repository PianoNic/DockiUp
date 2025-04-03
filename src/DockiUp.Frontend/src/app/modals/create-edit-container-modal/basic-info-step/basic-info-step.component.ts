import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, Signal } from '@angular/core';
import { FormControl, FormGroup, Validators, ReactiveFormsModule, ValidationErrors, AbstractControl } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

@Component({
  selector: 'app-basic-info-step',
  imports: [
    CommonModule,
    MatFormFieldModule,
    MatInputModule,
    ReactiveFormsModule
  ],
  templateUrl: './basic-info-step.component.html',
  styleUrl: './basic-info-step.component.scss'
})
export class BasicInfoStepComponent implements OnInit {
  @Input() formGroup!: FormGroup;
  @Input() containerNames!: Signal<(string | null)[]>;

  constructor() {
    this.formGroup = new FormGroup({
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

  ngOnInit() {
    const nameControl = this.formGroup.get('name');
    if (nameControl) {
      const currentValidators = nameControl.validator ? [nameControl.validator, this.nameExistsValidator()] : this.nameExistsValidator();
      nameControl.setValidators(currentValidators);
      nameControl.updateValueAndValidity();
    }
  }

  nameExistsValidator() {
    return (control: AbstractControl): ValidationErrors | null => {
      if (!control.value || !this.containerNames) {
        return null;
      }
      
      const name = control.value.toLowerCase();
      const exists = this.containerNames().some((containerName: string | null) => {
        return containerName && containerName.toLowerCase() === name;
      });
      
      return exists ? { nameExists: true } : null;
    };
  }
}