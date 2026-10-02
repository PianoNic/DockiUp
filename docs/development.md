# Development

The backend is .NET 10 (`src/DockiUp.API`), the frontend Angular 21 (`src/DockiUp.Frontend`).

## Run it

```bash
# 1. Database on localhost:5433
docker compose -f compose.dev.yml up -d

# 2. API on http://localhost:5098 (the vault key is needed for the secrets and Git credentials pages)
export Vault__MasterKey="$(openssl rand -base64 32)"
dotnet run --project src/DockiUp.API

# 3. Frontend on http://localhost:4200
cd src/DockiUp.Frontend
npm install
npm start
```

## Test

```bash
dotnet test src/DockiUp.Tests
```

## After changing the API

Regenerate the frontend client while the API runs:

```bash
cd src/DockiUp.Frontend
npm run apigen
```

## Database changes

After changing an entity, add a migration from the repository root:

```bash
dotnet ef migrations add <Name> --project src/DockiUp.Infrastructure --startup-project src/DockiUp.API
```

It's applied when the API starts.

## Build the image

```bash
docker build -f src/DockiUp.API/Dockerfile -t dockiup .
```
