import { CommonModule } from '@angular/common';
import { Component, inject, Input, OnInit, Signal, OnDestroy } from '@angular/core';
import { FormControl, FormGroup, Validators, ReactiveFormsModule, ValidationErrors, AbstractControl } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ContainerStore } from '../../../pages/dashboard/container.store';
import { Subject, debounceTime, distinctUntilChanged, filter, takeUntil } from 'rxjs';

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
export class BasicInfoStepComponent implements OnInit, OnDestroy {
  @Input() formGroup!: FormGroup;
  @Input() containerNames!: Signal<(string | null)[]>;
  containerStore = inject(ContainerStore);
  
  isValidatingGitUrl = false;
  isValidGitUrl: boolean | null = null;
  private destroy$ = new Subject<void>();
  
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
    
    this.formGroup.get('gitUrl')?.valueChanges
      .pipe(
        takeUntil(this.destroy$),
        debounceTime(500),
        distinctUntilChanged(),
        filter(url => !!url && url.length > 5)
      )
      .subscribe(url => {
        this.validateGitUrl(url);
      });

    const gitUrlControl = this.formGroup.get('gitUrl');
    if (gitUrlControl) {
      const validators = [];
      if (gitUrlControl.validator) {
        validators.push(gitUrlControl.validator);
      }
      validators.push(this.gitUrlValidator());
      gitUrlControl.setValidators(validators);
    }
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
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

  gitUrlValidator() {
    return (_control: AbstractControl): ValidationErrors | null => {
      if (this.isValidGitUrl === false && !this.isValidatingGitUrl) {
        return { invalidGitUrl: true };
      }
      return null;
    };
  }

  validateGitUrl(url: string) {
    this.isValidatingGitUrl = true;
    this.isValidGitUrl = null;
    
    this.formGroup.get('gitUrl')?.markAsPending();
    
    this.containerStore.IsValidGitRepository(url)
      .subscribe({
        next: (isValid) => {
          this.isValidGitUrl = isValid;
          this.isValidatingGitUrl = false;
          
          const gitUrlControl = this.formGroup.get('gitUrl');
          if (gitUrlControl) {
            gitUrlControl.updateValueAndValidity();
          }
        },
        error: () => {
          this.isValidGitUrl = false;
          this.isValidatingGitUrl = false;
          
          const gitUrlControl = this.formGroup.get('gitUrl');
          if (gitUrlControl) {
            gitUrlControl.updateValueAndValidity();
          }
        }
      });
  }
}