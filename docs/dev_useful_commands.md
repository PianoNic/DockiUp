# Useful Development Commands

[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF_Core-9.0-blue.svg)](https://docs.microsoft.com/en-us/ef/core/)
[![Docker](https://img.shields.io/badge/docker-required-blue.svg)](https://www.docker.com/)

This document contains essential commands and configurations for developers working on DockiUp.

## Development Environment Setup

### User Secrets Configuration

Add the following to your user secrets:

```json
{
  "ConnectionStrings": {
    "DockiUpDatabase": "Server=localhost;Port=3307;Database=dockiupdb-dev;User=root;Password=devPassword;"
  },
  "SystemPaths": {
    "DockerSocket": "",
    "ProjectsPath": ""
  },
  "JWT_SECRET_KEY": ""
}
```

To access user secrets in a .NET project:

```sh
dotnet user-secrets init --project src/DockiUp.API
dotnet user-secrets set "JWT_SECRET_KEY" "your-secret-key" --project src/DockiUp.API
```

## Docker Commands

### Start Development Database

```sh
docker compose -f compose.dev.yml up -d
```

### Stop Development Environment

```sh
docker compose -f compose.dev.yml down
```

### Rebuild Development Containers

```sh
docker compose -f compose.dev.yml up -d --build
```

### View Container Logs

```sh
docker compose -f compose.dev.yml logs -f
```

## Entity Framework (EF) Migrations

### Add a Migration

```sh
dotnet ef migrations add UpdatedUser -p src/DockiUp.Infrastructure -s src/DockiUp.API -o Migrations
```

### Apply Migrations

```sh
dotnet ef database update -p src/DockiUp.Infrastructure -s src/DockiUp.API
```

### Remove Last Migration

```sh
dotnet ef migrations remove -p src/DockiUp.Infrastructure -s src/DockiUp.API
```

### Generate SQL Script

```sh
dotnet ef migrations script -p src/DockiUp.Infrastructure -s src/DockiUp.API -o migration.sql
```
