# Getting started

DockiUp runs as one container next to a PostgreSQL database. You need Docker with Compose v2.

## Install

1. Create a folder and download the compose file and settings template:

   ```bash
   mkdir dockiup && cd dockiup
   curl -O https://raw.githubusercontent.com/PianoNic/DockiUp/main/compose.yml
   curl -o .env https://raw.githubusercontent.com/PianoNic/DockiUp/main/.env.example
   ```

2. Open `.env` and set a database password and a vault key:

   ```env
   POSTGRES_PASSWORD=pick-a-password
   VAULT_MASTER_KEY=paste-the-output-of: openssl rand -base64 32
   ```

3. Start it:

   ```bash
   docker compose up -d
   ```

Open <http://localhost:8080>.

## Update

```bash
docker compose pull
docker compose up -d
```

Database changes are applied automatically when DockiUp starts.

## Next steps

- [Manage stacks that already run on the host](/existing-stacks)
- [Add more hosts](/nodes)
- [Require sign-in](/sign-in)
