import { defineConfig } from 'vitepress'

// Docs site for DockiUp, built from the markdown in this folder. Served at the domain root on
// Cloudflare Pages, so no `base` is needed. Build: `vitepress build` (output: .vitepress/dist).
export default defineConfig({
  title: 'DockiUp',
  description: 'Self-hosted Docker Compose deployments: you commit, DockiUp pulls, builds and deploys.',
  lastUpdated: true,
  cleanUrls: true,
  ignoreDeadLinks: true,
  // Absolute, because a link preview is rendered by a crawler that has no page to resolve a
  // relative path against.
  head: [
    ['link', { rel: 'icon', href: '/favicon.svg' }],
    ['meta', { property: 'og:image', content: 'https://docs.dockiup.pianonic.ch/logo.png' }],
    ['meta', { property: 'og:url', content: 'https://docs.dockiup.pianonic.ch/' }],
  ],
  sitemap: { hostname: 'https://docs.dockiup.pianonic.ch' },
  themeConfig: {
    nav: [
      { text: 'Getting started', link: '/getting-started' },
      {
        text: 'Guides',
        items: [
          { text: 'Existing stacks', link: '/existing-stacks' },
          { text: 'More hosts', link: '/nodes' },
          { text: 'Sign-in', link: '/sign-in' },
          { text: 'Settings', link: '/settings' },
        ],
      },
      { text: 'Development', link: '/development' },
    ],
    sidebar: [
      {
        text: 'Setup',
        items: [
          { text: 'Getting started', link: '/getting-started' },
          { text: 'Settings', link: '/settings' },
        ],
      },
      {
        text: 'Guides',
        items: [
          { text: 'Existing stacks', link: '/existing-stacks' },
          { text: 'More hosts', link: '/nodes' },
          { text: 'Sign-in', link: '/sign-in' },
        ],
      },
      {
        text: 'Contributing',
        items: [
          { text: 'Development', link: '/development' },
          { text: 'Releasing', link: '/releasing' },
        ],
      },
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/PianoNic/DockiUp' },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/PianoNic/DockiUp/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    footer: {
      message: 'Made with care by PianoNic.',
      copyright: 'DockiUp',
    },
  },
})
