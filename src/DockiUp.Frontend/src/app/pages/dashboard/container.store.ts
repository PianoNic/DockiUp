import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withHooks,
  withMethods,
  withState,
  withComputed
} from '@ngrx/signals';
import { ContainerService, StatusType } from '../../api';
import { ContainerDto } from '../../api/model/containerDto';
import { SignalRService } from '../../services/signal-r.service';
import { ContainerStats } from '../../models/container-stats';

type ContainerState = {
  containers: ContainerDto[];
  loading: boolean;
  error: string | null;
};

export const initialContainerStore: ContainerState = {
  containers: [],
  loading: false,
  error: null
};

export const ContainerStore = signalStore(
  { providedIn: 'root' },
  withState(initialContainerStore),
  withComputed((store) => ({
    containerStats: computed(() => {
      const counts: ContainerStats = {
        total: store.containers().length,
        stopped: 0,
        running: 0,
        needsUpdate: 0,
        updating: 0,
        failed: 0
      };

      store.containers().forEach(container => {
        if (container.status === StatusType.Stopped) counts.stopped++;
        else if (container.status === StatusType.Running) counts.running++;
        else if (container.status === StatusType.NeedsUpdate) counts.needsUpdate++;
        else if (container.status === StatusType.Updating) counts.updating++;
        else if (container.status === StatusType.Failed) counts.failed++;
      });

      return counts;
    })
  })),
  withMethods((store) => {
    const containerService = inject(ContainerService);
    const signalRService = inject(SignalRService);
    return {
      loadContainers() {
        patchState(store, { loading: true, error: null });
        containerService.getContainers()
          .subscribe({
            next: (containers) => patchState(store, {
              containers,
              loading: false
            }),
            error: (_error) => patchState(store, {
              error: 'Failed to load containers',
              loading: false
            })
          });
      },
      updateContainer(updatedContainer: ContainerDto) {
        const currentContainers = store.containers();
        const updatedContainers = currentContainers.map(container =>
          container.dbContainerId === updatedContainer.dbContainerId ? updatedContainer : container
        );
        patchState(store, { containers: updatedContainers });
      },
      setupSignalRListener() {
        signalRService.listenForContainerUpdates((containerDto) => {
          this.updateContainer(containerDto);
        });
      }
    };
  }),
  withHooks((store) => ({
    onInit() {
      store.loadContainers();
      store.setupSignalRListener();
    }
  }))
);
