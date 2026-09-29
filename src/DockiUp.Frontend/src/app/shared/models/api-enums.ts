/**
 * App-side enums matching backend (DockiUp.Domain / DockiUp.Application).
 * Used when generated API client does not export these types.
 */

/** Container state (matches DockiUp.Application.Enums.UpdateMethodType). */
export const UpdateMethodType = {
  Unknown: 0,
  Created: 1,
  Stopped: 2,
  Running: 3,
  Updating: 4,
  Crashed: 5,
} as const;

export type UpdateMethodType = (typeof UpdateMethodType)[keyof typeof UpdateMethodType];

/** Project origin (matches DockiUp.Domain.Enums.ProjectOriginType). */
export const ProjectOriginType = {
  Unknown: 0,
  Git: 1,
  Compose: 2,
  Import: 3,
} as const;

export type ProjectOriginType = (typeof ProjectOriginType)[keyof typeof ProjectOriginType];

/** Project update method (matches DockiUp.Domain.Enums.ProjectUpdateMethod). */
export const ProjectUpdateMethod = {
  Unknown: 0,
  Webhook: 1,
  Manual: 2,
  Periodically: 3,
} as const;

export type ProjectUpdateMethod = (typeof ProjectUpdateMethod)[keyof typeof ProjectUpdateMethod];

/** Normalize container state from API (may be string due to JsonStringEnumConverter) to numeric UpdateMethodType. */
export function normalizeContainerState(state: number | string | undefined | null): number {
  if (state === undefined || state === null) return UpdateMethodType.Unknown;
  if (typeof state === 'number') return state;
  const s = String(state);
  switch (s) {
    case 'Running': return UpdateMethodType.Running;
    case 'Stopped': return UpdateMethodType.Stopped;
    case 'Created': return UpdateMethodType.Created;
    case 'Updating': return UpdateMethodType.Updating;
    case 'Crashed': return UpdateMethodType.Crashed;
    case 'Unknown': return UpdateMethodType.Unknown;
    default: return UpdateMethodType.Unknown;
  }
}

/** Lower-case display label for a container state; also the `data-state` value the global .state-badge colours. */
export function containerStateLabel(state: number | string | undefined): string {
  switch (normalizeContainerState(state)) {
    case UpdateMethodType.Created: return 'created';
    case UpdateMethodType.Stopped: return 'stopped';
    case UpdateMethodType.Running: return 'running';
    case UpdateMethodType.Updating: return 'updating';
    case UpdateMethodType.Crashed: return 'crashed';
    default: return 'unknown';
  }
}
