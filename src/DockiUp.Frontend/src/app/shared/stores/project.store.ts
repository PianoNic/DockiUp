import { signalStore, withHooks, withMethods, withState, withComputed, patchState } from '@ngrx/signals';
import { ContainerDto, ContainerService, DeploymentDto, ProjectDto, ProjectService, SetupProjectDto } from '../../api';
import { inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { NotificationService } from '../services/notification.service';

type ProjectState = {
  projectDtos: ProjectDto[],
  loading: boolean,
};

export const initialProjectStore: ProjectState = {
  projectDtos: [],
  loading: false
};

export const ProjectStore = signalStore(
  { providedIn: 'root' },
  withState(initialProjectStore),
  withComputed((store) => ({})),
  withMethods((store) => {
    const projectService = inject(ProjectService);
    const containerService = inject(ContainerService);
    const notifications = inject(NotificationService);

    const loadContainers = async () => {
      patchState(store, { loading: true });
      try {
        const projects = await firstValueFrom(projectService.getProjects()) ?? initialProjectStore.projectDtos;
        patchState(store, { projectDtos: projects });
      } catch (err) {
        notifications.showError('Failed to load projects', err);
      } finally {
        patchState(store, { loading: false });
      }
    };

    /** Update project list from SignalR payload (no HTTP call). */
    const setProjectDtos = (projects: ProjectDto[]) => {
      patchState(store, { projectDtos: projects ?? [] });
    };

    /** Creates the project and queues its first deployment. Resolves true on success. */
    const deployProject = async (setupProjectDto: SetupProjectDto): Promise<boolean> => {
      patchState(store, { loading: true });
      try {
        await firstValueFrom(projectService.deployProject(setupProjectDto));
        await loadContainers();
        return true;
      } catch (err) {
        notifications.showError('Failed to create project', err);
        return false;
      } finally {
        patchState(store, { loading: false });
      }
    };

    // Start/stop/restart work for every compose project (managed or not): by id when DockiUp knows it,
    // otherwise by docker name + the node it runs on.
    const lifecycle = async (project: ProjectDto, action: 'start' | 'stop' | 'restart') => {
      const call = { start: projectService.startProject, stop: projectService.stopProject, restart: projectService.restartProject }[action];
      try {
        await firstValueFrom(call.call(projectService, project.id ?? undefined, project.dockerProjectName, project.nodeId ?? undefined));
        await loadContainers();
      } catch (err) {
        notifications.showError(`Failed to ${action} ${project.projectName}`, err);
      }
    };

    /** `compose down` (+ volumes if asked); a DockiUp project is also forgotten. Resolves true on success. */
    const removeProject = async (project: ProjectDto, removeVolumes: boolean): Promise<boolean> => {
      try {
        await firstValueFrom(projectService.removeProject(project.id ?? undefined, project.dockerProjectName, project.nodeId ?? undefined, removeVolumes));
        notifications.success(`${project.projectName} removed`);
        await loadContainers();
        return true;
      } catch (err) {
        notifications.showError(`Failed to remove ${project.projectName}`, err);
        return false;
      }
    };

    const removeContainer = async (container: ContainerDto, nodeId?: string | null) => {
      try {
        await firstValueFrom(containerService.removeContainer(container.id, nodeId ?? undefined));
        notifications.success(`${container.name} removed`);
        await loadContainers();
      } catch (err) {
        notifications.showError(`Failed to remove ${container.name}`, err);
      }
    };

    /** Downloads newer images without restarting; the next deploy applies them. */
    const pullImages = async (projectId: string) => {
      try {
        const { output } = await firstValueFrom(projectService.pullProjectImages(projectId));
        // compose prints "Pulled" per service either way; only new layers mean something changed.
        const newer = /Download complete|Pull complete|Downloaded newer image/i.test(output);
        notifications.success(newer ? 'Newer images downloaded. Deploy to apply them.' : 'Images are already up to date.');
      } catch (err) {
        notifications.showError('Failed to pull images', err);
      }
    };

    /** Queues a deployment (git sync + compose pull/up); progress arrives live over SignalR. */
    const deployNow = async (projectId: string): Promise<DeploymentDto | null> => {
      try {
        const deployment = await firstValueFrom(projectService.queueDeployment(projectId));
        notifications.success('Deployment queued');
        return deployment;
      } catch (err) {
        notifications.showError('Failed to queue deployment', err);
        return null;
      }
    };

    return {
      loadContainers,
      setProjectDtos,
      deployProject,
      lifecycle,
      deployNow,
      removeProject,
      removeContainer,
      pullImages,
    };
  }),
  withHooks((store) => ({}))
);
