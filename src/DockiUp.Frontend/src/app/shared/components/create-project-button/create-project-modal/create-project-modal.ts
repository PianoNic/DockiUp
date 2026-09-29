import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators, FormGroup } from '@angular/forms';
import { MatDialogContent, MatDialogModule, MatDialogRef } from "@angular/material/dialog";
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatStepperModule } from '@angular/material/stepper';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatRadioModule } from '@angular/material/radio';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatSelectModule } from '@angular/material/select';
import { NodeDto, NodesService, SetupProjectDto } from '../../../../api';
import { ProjectOriginType, ProjectUpdateMethod, UpdateMethodType } from '../../../models/api-enums';


@Component({
  selector: 'app-create-project-modal',
  imports: [
    MatDialogModule,
    MatDialogContent,
    MatProgressBarModule,
    MatStepperModule,
    FormsModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatRadioModule,
    MatAutocompleteModule,
    MatSelectModule
  ],
  templateUrl: './create-project-modal.html',
  styleUrl: './create-project-modal.scss'
})
export class CreateProjectModal implements OnInit {
  public isloading = signal(false);
  private fb = inject(FormBuilder);
  private dialogRef = inject(MatDialogRef<CreateProjectModal>);
  private nodesService = inject(NodesService);

  // Online nodes the project can be deployed to; empty means single-host (local only).
  readonly nodes = signal<NodeDto[]>([]);

  // Enums for template
  readonly ProjectOriginType = ProjectOriginType;
  readonly ProjectUpdateMethod = ProjectUpdateMethod;

  // Autocomplete data
  readonly paths: string[] = [
    '/home/user/project1',
    '/var/www/app',
    '/opt/docker/project2',
    '/srv/data/project3',
    '/usr/local/share/project4'
  ];

  pathValue = signal('');
  filteredPaths = computed(() =>
    this.paths.filter(path =>
      path.toLowerCase().includes(this.pathValue().toLowerCase())
    )
  );

  // Form groups
  projectInformationFormGroup: FormGroup;
  projectOriginFormGroup: FormGroup;
  updateMethodFormGroup: FormGroup;

  // Signals for validation state to avoid ExpressionChangedAfterItHasBeenCheckedError
  isStep1Valid = signal(false);
  isStep2Valid = signal(false);
  isStep3Valid = signal(false);

  constructor() {
    // Initialize forms
    this.projectInformationFormGroup = this.fb.group({
      // Mirrors the server rule: it becomes a folder name and the compose project name.
      projectName: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9 _-]*$/)]],
      description: [''],
      // Empty string = deploy on the local control-plane host; a node id = deploy to that node.
      nodeId: ['']
    });

    this.projectOriginFormGroup = this.fb.group({
      originType: [ProjectOriginType.Compose, Validators.required],
      gitUrl: [''],
      branch: [''],
      composeFile: [''],
      composeContent: ['', Validators.required],
      path: ['']
    });

    this.updateMethodFormGroup = this.fb.group({
      updateMethod: [ProjectUpdateMethod.Webhook, Validators.required],
      periodicInterval: ['']
    });
  }

  ngOnInit() {
    // Offer online nodes as deploy targets; if none are connected the selector stays hidden.
    this.nodesService.apiNodesGet().subscribe({
      next: nodes => this.nodes.set(nodes.filter(n => n.online)),
      error: () => this.nodes.set([])
    });

    // Setup path autocomplete
    this.pathValue.set(this.projectOriginFormGroup.get('path')?.value || '');
    this.projectOriginFormGroup.get('path')?.valueChanges.subscribe(val => {
      this.pathValue.set(val || '');
    });

    // Initial validation setup
    this.updateValidationForOriginType(ProjectOriginType.Compose);
    // Initialize validation for preselected webhook
    this.updateValidationForUpdateMethod(ProjectUpdateMethod.Webhook);
    this.updateValidationStates();

    // Listen for form validation changes
    this.projectInformationFormGroup.statusChanges.subscribe(() => {
      this.updateValidationStates();
    });

    this.projectOriginFormGroup.statusChanges.subscribe(() => {
      this.updateValidationStates();
    });

    this.updateMethodFormGroup.statusChanges.subscribe(() => {
      this.updateValidationStates();
    });

    // Listen for origin type changes
    this.projectOriginFormGroup.get('originType')?.valueChanges.subscribe(originType => {
      this.updateValidationForOriginType(originType);
    });

    // Listen for update method changes
    this.updateMethodFormGroup.get('updateMethod')?.valueChanges.subscribe(updateMethod => {
      this.updateValidationForUpdateMethod(updateMethod);
    });
  }

  private updateValidationForOriginType(originType: ProjectOriginType) {
    const originControls = this.projectOriginFormGroup.controls;

    // Clear all validators first
    originControls['gitUrl'].clearValidators();
    originControls['composeContent'].clearValidators();
    originControls['path'].clearValidators();

    // Reset values for unused fields (but don't call updateValueAndValidity yet)
    originControls['gitUrl'].setValue('', { emitEvent: false });
    originControls['composeContent'].setValue('', { emitEvent: false });
    originControls['path'].setValue('', { emitEvent: false });

    // Reset update method form when origin type changes, but keep webhook preselected
    this.updateMethodFormGroup.get('updateMethod')?.setValue(ProjectUpdateMethod.Webhook, { emitEvent: false });
    this.updateMethodFormGroup.get('periodicInterval')?.setValue('', { emitEvent: false });

    // Apply validators based on origin type
    switch (originType) {
      case ProjectOriginType.Import:
        originControls['path'].setValidators([Validators.required]);
        break;

      case ProjectOriginType.Compose:
        originControls['composeContent'].setValidators([Validators.required]);
        break;

      case ProjectOriginType.Git:
        originControls['gitUrl'].setValidators([Validators.required]);
        break;
    }

    // Always require update method selection
    this.updateMethodFormGroup.get('updateMethod')?.setValidators([Validators.required]);

    // Update validity for relevant controls only
    originControls['gitUrl'].updateValueAndValidity();
    originControls['composeContent'].updateValueAndValidity();
    originControls['path'].updateValueAndValidity();
    this.updateMethodFormGroup.get('updateMethod')?.updateValueAndValidity();

    // Initialize validation for the preselected webhook
    this.updateValidationForUpdateMethod(ProjectUpdateMethod.Webhook);

    // Update validation states
    setTimeout(() => this.updateValidationStates(), 0);
  }

  private updateValidationForUpdateMethod(updateMethod: ProjectUpdateMethod) {
    const intervalControl = this.updateMethodFormGroup.get('periodicInterval');

    // Clear validators
    intervalControl?.clearValidators();

    // Reset values
    intervalControl?.setValue('', { emitEvent: false });

    // Apply validators based on update method
    switch (updateMethod) {
      case ProjectUpdateMethod.Periodically:
        intervalControl?.setValidators([Validators.required, Validators.min(1)]);
        break;

      case ProjectUpdateMethod.Manual:
        // No additional validation needed
        break;
    }

    // Update validity
    intervalControl?.updateValueAndValidity();

    // Update validation states
    setTimeout(() => this.updateValidationStates(), 0);
  }

  private updateValidationStates() {
    // Step 1 validation
    this.isStep1Valid.set(this.projectInformationFormGroup.valid);

    // Step 2 validation
    const originType = this.projectOriginFormGroup.get('originType')?.value;
    let step2Valid = false;

    switch (originType) {
      case ProjectOriginType.Import:
        step2Valid = !!this.projectOriginFormGroup.get('path')?.valid;
        break;
      case ProjectOriginType.Compose:
        step2Valid = !!this.projectOriginFormGroup.get('composeContent')?.valid;
        break;
      case ProjectOriginType.Git:
        step2Valid =
          !!this.projectOriginFormGroup.get('gitUrl')?.valid &&
          !!this.projectOriginFormGroup.get('composeContent')?.valid;
        break;
      default:
        step2Valid = false;
    }
    this.isStep2Valid.set(step2Valid);

    // Step 3 validation - always validate update method
    const updateMethod = this.updateMethodFormGroup.get('updateMethod')?.value;
    let step3Valid = false;

    if (!updateMethod) {
      step3Valid = false;
    } else {
      switch (updateMethod) {
        case ProjectUpdateMethod.Webhook:
          step3Valid = true; // DockiUp generates the webhook URL and secret
          break;
        case ProjectUpdateMethod.Periodically:
          step3Valid = !!this.updateMethodFormGroup.get('periodicInterval')?.valid;
          break;
        case ProjectUpdateMethod.Manual:
          step3Valid = true;
          break;
        default:
          step3Valid = false;
      }
    }
    this.isStep3Valid.set(step3Valid);
  }

  cancel() {
    this.dialogRef.close();
  }

  finish() {
    if (!this.isStep1Valid() || !this.isStep2Valid() || !this.isStep3Valid()) {
      return;
    }

    const info = this.projectInformationFormGroup.value;
    const origin = this.projectOriginFormGroup.value;
    const update = this.updateMethodFormGroup.value;

    const result: SetupProjectDto = {
      projectName: info.projectName ?? null,
      description: info.description ?? null,
      nodeId: info.nodeId || null,
      projectOrigin: origin.originType,
      gitUrl: origin.originType === ProjectOriginType.Git ? origin.gitUrl ?? null : null,
      // Git projects use the repo's own compose file; compose projects send the pasted content.
      branch: origin.originType === ProjectOriginType.Git ? origin.branch?.trim() || null : null,
      composeFile: origin.originType === ProjectOriginType.Git ? origin.composeFile?.trim() || null : null,
      compose: origin.originType === ProjectOriginType.Compose ? origin.composeContent ?? null : null,
      path: origin.originType === ProjectOriginType.Import ? origin.path ?? null : null,
      projectUpdateMethod: update.updateMethod,
      periodicIntervalInMinutes: update.updateMethod === ProjectUpdateMethod.Periodically
        ? (update.periodicInterval ? Number(update.periodicInterval) : null)
        : null
    };

    this.dialogRef.close(result);
  }
}
