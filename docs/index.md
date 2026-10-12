---
layout: home

hero:
  name: DockiUp
  text: Your little whale that ships your stacks
  tagline: Push a commit and DockiUp happily deploys your Docker Compose projects, on one host or many.
  image:
    src: /favicon.svg
    alt: DockiUp's whale
  actions:
    - theme: brand
      text: Let's get started
      link: /getting-started
    - theme: alt
      text: GitHub
      link: https://github.com/PianoNic/DockiUp

features:
  - title: Deploys from anywhere
    details: A Git repository, a compose file you write or upload, a single image, or a pasted docker run command.
  - title: Updates on your terms
    details: Push webhooks from GitHub, Gitea/Forgejo and GitLab, a schedule, or a button. Every deployment keeps its commit and log.
  - title: No moving house
    details: Compose projects already running on a host are picked up automatically, right where they are.
  - title: Many hosts, one home
    details: Add a node with one compose file. It swims over to DockiUp by itself, so the node needs no open ports.
  - title: Fresh images
    details: Newer images for the tags you run are spotted for you. Get a heads-up, or let DockiUp deploy them.
  - title: Files, logs and secrets
    details: Edit compose files with completion, read coloured logs, open a shell, and keep secrets encrypted in the vault.
---
