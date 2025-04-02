# Development setup instructions

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
    "DockiUpDatabase": "Server=localhost;Port=3307;Database=dockiupdb-dev;User=postgres;Password=d4vpas8w0rd13!!!;"
  },
  "SystemPaths": {
    "DockerSocket": "",
    "ProjectsPath": ""
  },
  "JWT_SECRET_KEY": ""
}
```

## Docker Commands

### Start Development Database

```sh
docker compose -f compose.dev.yml up -d
```

## Entity Framework (EF) Migrations

### Add a Migration

```sh
dotnet ef migrations add Changes
```
