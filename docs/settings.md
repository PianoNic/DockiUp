# Settings

Most installs only need the three settings in `.env`. Everything else has a sensible default.

## Basics

Set these in `.env`, next to `compose.yml`.

| Setting | Needed | What it does |
|---|---|---|
| `POSTGRES_PASSWORD` | yes | Password of DockiUp's database |
| `VAULT_MASTER_KEY` | yes | Encrypts stored secrets and Git credentials. Create it with `openssl rand -base64 32` and keep it safe: without it they can't be read |
| `PUBLIC_URL` | for [more hosts](/nodes) | Address DockiUp is reached at, e.g. `https://dockiup.example.com`. Used for node setup and the webhook URLs DockiUp shows you |

## Advanced

To change one of these, add it to the `environment` of the `dockiup` service in `compose.yml`:

```yaml
services:
  dockiup:
    environment:
      ImageUpdates__IntervalHours: 12
```

| Setting | Default | What it does |
|---|---|---|
| `ImageUpdates__IntervalHours` | `6` | How often running images are checked for updates. `0` turns the automatic check off; **Check for updates** in the UI still works |
| `SystemPaths__ProjectsPath` | `/data/dockiup/projects` (set in `compose.yml`) | Where DockiUp keeps the projects it creates. Mount it at the same path inside and out |
| `SystemPaths__DockerSocket` | the mounted socket | Another Docker endpoint, e.g. `tcp://docker-proxy:2375` for a socket proxy |
| `ConnectionStrings__DockiUpDatabase` | built from `POSTGRES_PASSWORD` | Use your own PostgreSQL: `Host=…;Port=5432;Database=dockiup;Username=…;Password=…` |
| `CORS_ALLOWED_ORIGINS` | none | Only needed when the web UI is served from another address than the API. A warning about it in the log is harmless otherwise |
| `DockiUp__PublicUrl` | none | Same as `PUBLIC_URL`; whichever you prefer |

## Sign-in

All optional; see [Sign-in](/sign-in) for the setup.

| Setting | Default | What it does |
|---|---|---|
| `Oidc__Authority` | none | Your provider's issuer URL. Setting it turns sign-in on |
| `Oidc__ClientId` | none | The client you created for DockiUp |
| `Oidc__PublicUrl` | the request's address | Address DockiUp is reached at, used for the sign-in redirect. Recommended in production |
| `Oidc__RoleClaim` | `roles` | Claim that holds roles. Use `groups` for Pocket ID, Authentik and Entra |
| `Oidc__InternalAuthority` | `Oidc__Authority` | How DockiUp itself reaches the provider, when that differs (e.g. over a Docker network) |
| `Oidc__Scope` | `openid profile email roles` | Scopes requested at sign-in |

DockiUp uses [Toamaisutaa](https://docs.toamaisutaa.pianonic.ch/oidc#configuration) for sign-in; every option
listed there works here too, written as `Oidc__<Key>`.

## Nodes

The compose file from **Add node** already fills these in. You only need them to change a node by hand.

| Setting | What it does |
|---|---|
| `Node__ServerUrl` | Address of your DockiUp. Setting it runs the container as a node instead of a full DockiUp |
| `Node__Token` | The node's token from **Add node** |
| `Node__Name` | Name the node reports. The name given in **Add node** takes precedence |
| `SystemPaths__ProjectsPath` | Where the node keeps project files (`/data/dockiup/projects`) |
