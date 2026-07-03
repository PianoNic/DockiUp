export interface NavigationItem {
  label: string;
  icon: string;
  route: string;
}

export const NAVIGATION_ITEMS: NavigationItem[] = [
  { label: 'Dashboard', icon: 'dashboard', route: '/dashboard' },
  { label: 'Containers', icon: 'view_list', route: '/containers' },
  { label: 'Activity', icon: 'history', route: '/activity' },
];
