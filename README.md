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
  <a href="#quick-start"><img src="https://img.shields.io/badge/Selfhost-Instructions-006db8.svg" alt="Self-hosting"/></a>
  <a href="#development"><img src="https://img.shields.io/badge/Development-Setup-006db8.svg" alt="Development"/></a>
</p>

---

> **Heads up:** DockiUp is in active development. Expect breaking changes before 1.0.
> **PolyForm Noncommercial 1.0.0** - free for noncommercial use; commercial use needs a separate
> licence. Not an OSI-approved open-source licence.

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

DockiUp needs Docker with Compose v2 on the host.

**1. Get the code and create `.env`:**

```bash
git clone https://github.com/PianoNic/DockiUp.git
cd DockiUp
cp .env.example .env
```

In `.env`, set at least `POSTGRES_PASSWORD` and a vault key:

```env
POSTGRES_PASSWORD=change-me
# 32 random bytes, base64: openssl rand -base64 32
VAULT_MASTER_KEY=
```

**2. Start it:**

```bash
docker compose up -d
```

Open <http://localhost:8080>.

### Manage stacks that already run on the host

DockiUp adopts every compose project it can read. Mount the folder your stacks live in at the **same
path** inside the DockiUp container, for example in `compose.yml`:

```yaml
services:
  dockiup:
    volumes:
      - /opt/stacks:/opt/stacks
```

Projects whose folder isn't mounted still show up, and say which folder to mount.

### Add another host

Set `PUBLIC_URL` in `.env` to the address other hosts reach DockiUp at, then press **Add node** on the
Nodes page. It hands you a ready-to-run compose file with a fresh token for the new host.

## Configuration

Everything is set in `.env`. See [`.env.example`](.env.example) for the full list.

| Variable | Required | Description |
|---|---|---|
| `POSTGRES_PASSWORD` | yes | Password of the bundled PostgreSQL database |
| `VAULT_MASTER_KEY` | for secrets and Git credentials | Base64-encoded 32-byte key that encrypts the vault |
| `PUBLIC_URL` | for nodes | URL other hosts reach DockiUp at; must not be `localhost` |
| `CORS_ALLOWED_ORIGINS` | in development | Origins allowed to call the API when the frontend runs separately |
| `Oidc__Authority`, `Oidc__ClientId` | no | Enable sign-in with an OpenID Connect provider; leave empty to run without login |

## Development

The backend is .NET 10 (`src/DockiUp.API`), the frontend Angular 21 with Material 3 (`src/DockiUp.Frontend`).

```bash
# Database only, on localhost:5433
docker compose -f compose.dev.yml up -d

# API on http://localhost:5098
dotnet run --project src/DockiUp.API

# Frontend on http://localhost:4200
cd src/DockiUp.Frontend && npm install && npm start
```

Tests: `dotnet test src/DockiUp.Tests`. After changing an endpoint or DTO, regenerate the frontend client
with `npm run apigen` while the API runs. More in the [development guide](docs/dev_setup.md); releases are
described in [releasing](docs/releasing.md).

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md).
