---
layout: home

hero:
  name: DockiUp
  text: You commit, DockiUp deploys.
  tagline: Self-hosted Docker Compose deployments with a web UI, for one host or many.
  image:
    src: /favicon.svg
    alt: DockiUp
  actions:
    - theme: brand
      text: Getting started
      link: /getting-started
    - theme: alt
      text: GitHub
      link: https://github.com/PianoNic/DockiUp

features:
  - title: Deploy from anywhere
    details: A Git repository, a compose file you write or upload, a single image, or a pasted docker run command.
  - title: Updates on your terms
    details: Push webhooks from GitHub, Gitea/Forgejo and GitLab, a schedule, or a button. Every deployment keeps its commit and log.
  - title: No migration
    details: Compose projects already running on a host are picked up automatically, in place.
  - title: Several hosts
    details: Add a node with one compose file. It connects to DockiUp by itself, so the node needs no open ports.
  - title: Image updates
    details: Newer images for the tags you run are detected. Get notified or deploy them automatically.
  - title: Files, logs and secrets
    details: Edit compose files with completion, read coloured logs, open a shell, and keep secrets in an encrypted vault.
---
