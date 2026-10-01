import { Injectable, inject, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ContainerStatsDto, DeploymentDto, ProjectDto } from '../../api';
import { ProjectStore } from '../stores/project.store';
import { ImageUpdateStore } from '../stores/image-update.store';
import { ImageUpdateDto } from '../../api';
import { ContainerStatsStore } from '../stores/container-stats.store';

const HUB_METHOD_CONTAINERS_CHANGED = 'ContainersChanged';

@Injectable({
  providedIn: 'root',
})
export class DockiUpHubService implements OnDestroy {
  private readonly projectStore = inject(ProjectStore);
  private readonly oidc = inject(OidcSecurityService);
  private readonly imageUpdates = inject(ImageUpdateStore);
  private readonly containerStats = inject(ContainerStatsStore);
  private hub: signalR.HubConnection | null = null;

  /** A deployment was queued, started or finished (any project). */
  readonly deploymentChanged$ = new Subject<DeploymentDto>();
  /** One live log line of a running deployment. */
  readonly deploymentLog$ = new Subject<{ deploymentId: string; line: string }>();

  constructor() {
    this.connect();
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private get hubUrl(): string {
    const base = environment.apiBaseUrl ?? '';
    const trimmed = base.replace(/\/$/, '');
    return `${trimmed}/hubs/dockiup`;
  }

  private async connect(): Promise<void> {
    if (this.hub) return;
    this.hub = new signalR.HubConnectionBuilder()
      // accessTokenFactory feeds the OIDC token to the hub as ?access_token= (SignalR can't set
      // Authorization on the socket). Empty in open mode, where the hub accepts anonymous connections.
      .withUrl(this.hubUrl, {
        withCredentials: true,
        accessTokenFactory: async () => (await firstValueFrom(this.oidc.getAccessToken())) ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.hub.on(HUB_METHOD_CONTAINERS_CHANGED, (projects: ProjectDto[]) => {
      this.projectStore.setProjectDtos(projects ?? []);
    });

    this.hub.on('DeploymentChanged', (d: DeploymentDto) => {
      this.deploymentChanged$.next(d);
      // A finished deployment changes what's live (deployed commit, containers): refresh the projects.
      if (d.status === 'Succeeded' || d.status === 'Failed') void this.projectStore.loadContainers();
    });
    this.hub.on('DeploymentLog', (deploymentId: string, line: string) => this.deploymentLog$.next({ deploymentId, line }));
    // Live container stats (every sampling round, all hosts).
    this.hub.on('ContainerStats', (stats: ContainerStatsDto[]) => this.containerStats.set(stats ?? []));

    // Image updates (#68): results of every check; a finished deployment may have applied some.
    this.hub.on('ImageUpdatesChanged', (updates: ImageUpdateDto[]) => this.imageUpdates.set(updates ?? []));
    this.hub.on('DeploymentChanged', (d: DeploymentDto) => {
      if (d.status === 'Succeeded' || d.status === 'Failed') void this.imageUpdates.load();
    });

    try {
      await this.hub.start();
    } catch (err) {
      console.warn('SignalR connection failed:', err);
    }
  }

  private stop(): void {
    if (this.hub) {
      void this.hub.stop();
      this.hub = null;
    }
  }
}
