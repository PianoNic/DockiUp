# Useful Development Commands

## Docker

### Start Development Database
```sh
docker compose -f compose.dev.yml up -d
```

## Entity Framework (EF) Migrations

### Add a Migration
```sh
dotnet ef migrations add UpdatedUser -p src/DockiUp.Infrastructure -s src/DockiUp.API -o Migrations
```
