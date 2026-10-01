import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { ComposeValidationDto, GitCredentialDto, GitCredentialsService, NodeDto, NodesService, ProjectDto, ProjectService, RepositoryInspectionDto, SetupProjectDto } from '../../../../api';
import { ProjectOriginType, ProjectUpdateMethod } from '../../../models/api-enums';
import { errorMessage } from '../../../services/notification.service';
import { ProjectStore } from '../../../stores/project.store';

/** What the dialog closes with: the compose project name to open (its first deployment streams there). */
export interface CreateProjectResult { dockerProjectName: string; }

type Source = 'git' | 'compose' | 'image' | 'existing';
type ComposeMode = 'editor' | 'run';

// Mirrors the server limits (DeployProjectCommandValidator).
const MAX_COMPOSE_CHARS = 100_000;
const MAX_ENV_CHARS = 50_000;
// Mirrors the server rule: it becomes a folder name and the compose project name.
const PROJECT_NAME = /^[A-Za-z0-9][A-Za-z0-9 _-]*$/;

/** New project flow: Source -> Where & environment -> Updates -> Review (validated with
 * `docker compose config` on the target host before anything is created). */
@Component({
  selector: 'app-create-project-modal',
  imports: [
    MatButtonModule, MatButtonToggleModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressBarModule, MatProgressSpinnerModule, MatRadioModule, MatSelectModule, MatStepperModule, MatTooltipModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './create-project-modal.html',
  styleUrl: './create-project-modal.scss',
})
export class CreateProjectModal implements OnInit {
  private readonly dialogRef = inject<MatDialogRef<CreateProjectModal, CreateProjectResult | undefined>>(MatDialogRef);
  private readonly nodesService = inject(NodesService);
  private readonly projectService = inject(ProjectService);
  private readonly gitCredentialsService = inject(GitCredentialsService);
  private readonly projectStore = inject(ProjectStore);
  private readonly stepper = viewChild.required<MatStepper>('stepper');

  readonly ProjectUpdateMethod = ProjectUpdateMethod;
  readonly restartPolicies = ['', 'no', 'always', 'unless-stopped', 'on-failure'];

  // Every node (offline ones too, to name the host of a running project); only online ones are targets.
  readonly allNodes = signal<NodeDto[]>([]);
  readonly nodes = computed(() => this.allNodes().filter(n => n.online));

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  // ---- Step 1: source ----
  readonly source = signal<Source>('git');

  readonly gitUrl = signal('');
  // Stored credentials for private repositories (managed under Settings); '' = public.
  readonly gitCredentials = signal<GitCredentialDto[]>([]);
  readonly gitCredentialId = signal('');
  readonly repo = signal<RepositoryInspectionDto | null>(null);
  readonly inspectedUrl = signal('');
  readonly branch = signal('');
  readonly composeFile = signal('');
  readonly selectedComposeFile = computed(() => this.repo()?.composeFiles.find(f => f.path === this.composeFile()) ?? null);

  readonly composeMode = signal<ComposeMode>('editor');
  readonly compose = signal('');
  readonly dockerRun = signal('');
  readonly conversionWarnings = signal<string[]>([]);

  readonly image = signal('');
  readonly tag = signal('');
  readonly ports = signal('');
  readonly volumes = signal('');
  readonly environment = signal('');
  readonly restart = signal('unless-stopped');
  readonly generatedCompose = signal('');

  readonly existingName = signal('');
  // Running compose projects DockiUp doesn't manage yet, from the shared project list.
  readonly adoptable = computed(() => this.projectStore.projectDtos()
    .filter(p => !p.managedByDockiUp)
    .sort((a, b) => this.hostName(a.nodeId).localeCompare(this.hostName(b.nodeId)) || a.dockerProjectName.localeCompare(b.dockerProjectName)));
  readonly existing = computed(() => this.adoptable().find(p => this.adoptKey(p) === this.existingName()) ?? null);

  // ---- Step 2: where & environment ----
  readonly projectName = signal('');
  readonly description = signal('');
  readonly nodeId = signal('');
  readonly envFile = signal('');

  // ---- Step 3: updates ----
  readonly updateMethod = signal<number>(ProjectUpdateMethod.Webhook);
  readonly interval = signal('');

  // ---- Step 4: review ----
  readonly validation = signal<ComposeValidationDto | null>(null);
  readonly validating = signal(false);

  readonly sourceValid = computed(() => {
    switch (this.source()) {
      case 'git': return !!this.repo() && this.inspectedUrl() === this.gitUrl().trim() && !!this.selectedComposeFile();
      case 'compose': return this.composeMode() === 'editor' && !!this.compose().trim() && this.compose().length <= MAX_COMPOSE_CHARS;
      case 'image': return !!this.image().trim();
      case 'existing': return !!this.existing()?.composeWorkingDir;
    }
  });
  readonly nameError = computed(() => {
    const name = this.projectName().trim();
    if (this.source() === 'existing') return null;
    if (!name) return 'Project name is required';
    if (name.length > 100) return 'Project name must be 100 characters or less';
    if (!PROJECT_NAME.test(name)) return 'Letters, digits, spaces, - and _ only; start with a letter or digit';
    return null;
  });
  readonly whereValid = computed(() => !this.nameError() && this.envFile().length <= MAX_ENV_CHARS);
  readonly updatesValid = computed(() =>
    this.updateMethod() !== ProjectUpdateMethod.Periodically || Number(this.interval()) >= 1);
  readonly canCreate = computed(() => this.sourceValid() && this.whereValid() && this.updatesValid() && !this.busy() && !this.validating()
    && (this.source() === 'existing' || !!this.validation()?.valid));

  /** The compose content that will be stored (compose and image sources). */
  private readonly composeContent = computed(() => this.source() === 'image' ? this.generatedCompose() : this.compose());

  ngOnInit() {
    this.gitCredentialsService.listGitCredentials().subscribe({
      next: credentials => this.gitCredentials.set(credentials),
      error: () => this.gitCredentials.set([]),
    });
    this.nodesService.apiNodesGet().subscribe({
      next: nodes => this.allNodes.set(nodes),
      error: () => this.allNodes.set([]),
    });
    // Fresh list of running projects for the "existing" source.
    this.projectStore.loadContainers();
  }

  hostName(nodeId?: string | null): string {
    if (!nodeId) return 'Local (this host)';
    return this.allNodes().find(n => n.id === nodeId)?.name ?? 'Node';
  }

  adoptKey(p: ProjectDto): string {
    return `${p.nodeId ?? ''}/${p.dockerProjectName}`;
  }

  setSource(source: Source) {
    this.source.set(source);
    this.error.set(null);
    this.validation.set(null);
  }

  // ---- Git ----

  /** A different credential can make a private repo readable: inspect again. */
  changeCredential(id: string) {
    this.gitCredentialId.set(id);
    if (this.gitUrl().trim()) this.inspect();
  }

  async inspect(branch?: string) {
    const url = this.gitUrl().trim();
    if (!url) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const repo = await firstValueFrom(this.projectService.inspectRepository({ gitUrl: url, branch: branch ?? null, gitCredentialId: this.gitCredentialId() || null }));
      this.repo.set(repo);
      this.inspectedUrl.set(url);
      this.branch.set(repo.branch);
      this.composeFile.set(repo.defaultComposeFile ?? '');
      this.validation.set(null);
      if (!this.projectName()) this.projectName.set(this.nameFromUrl(url));
    } catch (err) {
      this.repo.set(null);
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  changeBranch(branch: string) {
    if (branch !== this.branch()) this.inspect(branch);
  }

  private nameFromUrl(url: string): string {
    const last = url.replace(/\/+$/, '').split(/[/:]/).pop() ?? '';
    return last.replace(/\.git$/, '').replace(/[^A-Za-z0-9 _-]/g, '-').replace(/^[^A-Za-z0-9]+/, '').slice(0, 100);
  }

  // ---- Compose ----

  async uploadCompose(event: Event) {
    const text = await this.readUpload(event, ['.yml', '.yaml'], MAX_COMPOSE_CHARS, 'compose file');
    if (text !== null) {
      this.compose.set(text);
      this.composeMode.set('editor');
    }
  }

  async convertDockerRun() {
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(this.projectService.convertDockerRun({ command: this.dockerRun() }));
      this.compose.set(result.compose);
      this.conversionWarnings.set(result.warnings);
      this.composeMode.set('editor');
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  // ---- Image ----

  private async generateImageCompose(): Promise<boolean> {
    const lines = (text: string) => text.split('\n').map(l => l.trim()).filter(Boolean);
    try {
      const result = await firstValueFrom(this.projectService.generateImageCompose({
        image: this.image().trim(),
        tag: this.tag().trim() || null,
        ports: lines(this.ports()),
        volumes: lines(this.volumes()),
        environment: lines(this.environment()),
        restart: this.restart() || null,
      }));
      this.generatedCompose.set(result.compose);
      return true;
    } catch (err) {
      this.error.set(errorMessage(err));
      return false;
    }
  }

  // ---- .env ----

  async uploadEnv(event: Event) {
    const text = await this.readUpload(event, ['.env', '.txt', ''], MAX_ENV_CHARS, '.env file');
    if (text !== null) this.envFile.set(text);
  }

  private async readUpload(event: Event, extensions: string[], maxChars: number, what: string): Promise<string | null> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // picking the same file again should fire again
    if (!file) return null;
    const dot = file.name.lastIndexOf('.');
    const ext = dot > 0 ? file.name.slice(dot).toLowerCase() : file.name.startsWith('.') ? file.name.toLowerCase() : '';
    if (!extensions.includes(ext)) {
      this.error.set(`${file.name} is not a ${what} (${extensions.filter(Boolean).join(', ')}).`);
      return null;
    }
    if (file.size > maxChars * 4) {
      this.error.set(`${file.name} is too large (max ${Math.round(maxChars / 1000)} KB).`);
      return null;
    }
    const text = await file.text();
    if (text.length > maxChars) {
      this.error.set(`${file.name} is too large (max ${Math.round(maxChars / 1000)} KB).`);
      return null;
    }
    this.error.set(null);
    return text;
  }

  // ---- Navigation ----

  async next() {
    const index = this.stepper().selectedIndex;
    this.error.set(null);
    if (index === 0 && this.source() === 'image') {
      this.busy.set(true);
      const ok = await this.generateImageCompose();
      this.busy.set(false);
      if (!ok) return;
    }
    // The stepper is linear: let the [completed] bindings settle before moving on.
    setTimeout(() => this.stepper().next());
  }

  // Anything may have changed on the way back and forth: always re-check on reaching the review.
  onStepChange(index: number) {
    if (index === 3) this.validate();
  }

  async validate() {
    if (this.source() === 'existing') return;
    const run = ++this.validationRun; // only the latest run may report
    this.validating.set(true);
    this.validation.set(null);
    try {
      const git = this.source() === 'git';
      const result = await firstValueFrom(this.projectService.validateCompose({
        nodeId: this.nodeId() || null,
        compose: git ? null : this.composeContent(),
        envFile: this.envFile() || null,
        gitUrl: git ? this.gitUrl().trim() : null,
        gitCredentialId: git ? this.gitCredentialId() || null : null,
        branch: git ? this.branch() : null,
        composeFile: git ? this.composeFile() : null,
      }));
      if (run === this.validationRun) this.validation.set(result);
    } catch (err) {
      if (run === this.validationRun) this.validation.set({ valid: false, errors: [errorMessage(err)], warnings: [], services: [] });
    } finally {
      if (run === this.validationRun) this.validating.set(false);
    }
  }
  private validationRun = 0;

  cancel() {
    this.dialogRef.close();
  }

  async create() {
    if (!this.canCreate()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const periodic = this.updateMethod() === ProjectUpdateMethod.Periodically ? Number(this.interval()) : null;
      if (this.source() === 'existing') {
        const project = this.existing()!;
        const adopted = await firstValueFrom(this.projectService.adoptProject({
          dockerProjectName: project.dockerProjectName,
          nodeId: project.nodeId ?? null,
          description: this.description().trim() || null,
          projectUpdateMethod: this.updateMethod(),
          periodicIntervalInMinutes: periodic,
        }));
        await this.projectStore.loadContainers();
        this.dialogRef.close({ dockerProjectName: adopted.dockerProjectName });
        return;
      }

      const git = this.source() === 'git';
      const dto: SetupProjectDto = {
        projectName: this.projectName().trim(),
        description: this.description().trim() || null,
        nodeId: this.nodeId() || null,
        projectOrigin: git ? ProjectOriginType.Git : ProjectOriginType.Compose,
        gitUrl: git ? this.gitUrl().trim() : null,
        gitCredentialId: git ? this.gitCredentialId() || null : null,
        branch: git ? this.branch() : null,
        composeFile: git ? this.composeFile() : null,
        compose: git ? null : this.composeContent(),
        envFile: this.envFile() || null,
        projectUpdateMethod: this.updateMethod(),
        periodicIntervalInMinutes: periodic,
      };
      if (await this.projectStore.deployProject(dto))
        this.dialogRef.close({ dockerProjectName: dto.projectName.toLowerCase().replace(/\s+/g, '') });
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
