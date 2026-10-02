<p align="center">
  <img src="assets/DockiUpLogo.png" width="520" alt="DockiUp Logo">
</p>

<p align="center">
  <strong>Self-hosted Docker Compose deployments: you commit, DockiUp pulls, builds and deploys.</strong>
</p>

<p align="center">
  <a href="https://github.com/PianoNic/DockiUp"><img src="https://badgetrack.pianonic.ch/badge?tag=dockiup&label=visits&color=006db8&style=flat" alt="visits"/></a>
  <a href="https://github.com/PianoNic/DockiUp/releases"><img src="https://img.shields.io/github/v/release/PianoNic/DockiUp?include_prereleases&color=006db8&label=Latest%20Release" alt="Latest release"/></a>
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/License-PolyForm%20Noncommercial-006db8.svg" alt="PolyForm Noncommercial"/></a>
  <a href="https://docs.dockiup.pianonic.ch/getting-started"><img src="https://img.shields.io/badge/Selfhost-Instructions-006db8.svg" alt="Self-hosting"/></a>
  <a href="https://docs.dockiup.pianonic.ch/development"><img src="https://img.shields.io/badge/Development-Setup-006db8.svg" alt="Development"/></a>
</p>

---

## Screenshots

<p align="center">
  <img src="assets/screenshots/dashboard-light.png" width="49%" alt="Dashboard, light mode" />
  <img src="assets/screenshots/dashboard-dark.png" width="49%" alt="Dashboard, dark mode" />
</p>
<p align="center">
  <img src="assets/screenshots/project-light.png" width="49%" alt="Project with containers and coloured logs, light mode" />
  <img src="assets/screenshots/project-dark.png" width="49%" alt="Project with containers and coloured logs, dark mode" />
</p>

<details>
<summary><strong>More screenshots</strong></summary>

<p align="center">
  <img src="assets/screenshots/containers.png" width="49%" alt="All containers, grouped by project and host" />
  <img src="assets/screenshots/deployments.png" width="49%" alt="Deployment history with commits and logs" />
</p>
<p align="center">
  <img src="assets/screenshots/editor.png" width="49%" alt="Editing a compose file with schema completion" />
  <img src="assets/screenshots/new-project.png" width="49%" alt="New project dialog" />
</p>
<p align="center">
  <img src="assets/screenshots/nodes.png" width="49%" alt="Remote nodes" />
</p>

</details>

## Features

- **Deploy from anywhere**: a Git repository (public or private), a compose file you write or upload, a
  single image, or a pasted `docker run` command.
- **Updates on your terms**: push webhooks from GitHub, Gitea/Forgejo and GitLab, a schedule, or a button.
  Every deployment is queued and kept with its commit and full log.
- **Existing stacks, no migration**: compose projects already running on a host are picked up
  automatically, in place. Nothing is moved, copied or restarted.
- **Several hosts**: add a node with one compose file. It dials back to DockiUp, so the node needs no
  open ports.
- **Image updates**: newer images for the tags you run are detected; get notified or deploy them
  automatically, and pin a service to another tag.
- **Files in the browser**: edit compose and config files with completion, hover help and checks from the
  official compose schema. In Git projects, saving commits and pushes.
- **Logs and terminal**: coloured logs with search and local timestamps, a shell into any container, and
  live CPU and memory.
- **Secrets**: an encrypted vault whose values reach compose through an env file and never appear in
  logs.
- **Notifications**: Discord, Slack, Telegram, ntfy or any webhook, for deployments, nodes, image updates
  and cleanups.
- **Housekeeping**: images, volumes and networks per host, pruning, and scheduled cleanup.
- **Optional sign-in**: any OpenID Connect provider, or none.

## Quick start

```bash
mkdir dockiup && cd dockiup
curl -O https://raw.githubusercontent.com/PianoNic/DockiUp/main/compose.yml
curl -o .env https://raw.githubusercontent.com/PianoNic/DockiUp/main/.env.example
```

Set `POSTGRES_PASSWORD` and `VAULT_MASTER_KEY` in `.env` (create the key with `openssl rand -base64 32`), then:

```bash
docker compose up -d
```

Open <http://localhost:8080>.

## Documentation

**[docs.dockiup.pianonic.ch](https://docs.dockiup.pianonic.ch)**

- [Getting started](https://docs.dockiup.pianonic.ch/getting-started)
- [Existing stacks](https://docs.dockiup.pianonic.ch/existing-stacks)
- [More hosts](https://docs.dockiup.pianonic.ch/nodes)
- [Development](https://docs.dockiup.pianonic.ch/development)

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md).
