import { Injectable, inject, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ProjectDto } from '../../api';
import { ProjectStore } from '../stores/project.store';

const HUB_METHOD_CONTAINERS_CHANGED = 'ContainersChanged';

@Injectable({
  providedIn: 'root',
})
export class DockiUpHubService implements OnDestroy {
  private readonly projectStore = inject(ProjectStore);
  private readonly oidc = inject(OidcSecurityService);
  private hub: signalR.HubConnection | null = null;

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
