# Settings

All settings live in `.env`, next to `compose.yml`.

| Setting | Needed | What it does |
|---|---|---|
| `POSTGRES_PASSWORD` | yes | Password of DockiUp's database |
| `VAULT_MASTER_KEY` | yes | Encrypts stored secrets and Git credentials. Keep it safe: without it they can't be read |
| `PUBLIC_URL` | for [more hosts](/nodes) | Address other hosts reach DockiUp at |

Sign-in is set up separately, see [Sign-in](/sign-in).
