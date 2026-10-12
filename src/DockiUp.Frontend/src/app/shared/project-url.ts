/** URL key of a project: its compose name plus a short hash of its host, so `krint` on two hosts gets two URLs.
 *  Stable, so links and bookmarks keep working. */
export function projectSlug(dockerProjectName: string, nodeId?: string | null): string {
  let h = 0x811c9dc5; // FNV-1a
  for (const c of nodeId ?? 'local') h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  return `${dockerProjectName}-${(h >>> 0).toString(36).padStart(6, '0').slice(-6)}`;
}

/** Router link to a project's page. */
export function projectLink(dockerProjectName: string, nodeId?: string | null): string[] {
  return ['/project', projectSlug(dockerProjectName, nodeId)];
}
