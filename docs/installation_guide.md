# Installation Guide

[![Docker](https://img.shields.io/badge/docker-required-blue.svg)](https://www.docker.com/)
[![Database](https://img.shields.io/badge/database-MySQL-orange.svg)](https://www.mysql.com/)

This guide will walk you through the process of installing and configuring DockiUp in a production environment.

## Prerequisites

- Docker and Docker Compose installed
- Basic understanding of Docker
- Accessible ports for the application (default: 8080)
- Git installed (for cloning the repository)

## 1. Setup Environment Variables

Rename `template.env` to `.env` and fill in the required values:

```bash
cp template.env .env
nano .env
```

Ensure you configure the following core settings:

```ini
# Database Password
MYSQL_ROOT_PASSWORD=your-secure-database-password

# Application Settings
JWT_SECRET_KEY=your-secure-jwt-key

# Storage Paths
DOCKER_SOCKET_PATH="/your/docker/socket/path"
PROJECTS_PATH="/your/projects/path"
```

## 2. Start the Application

Run the following command to start all services in detached mode:

```sh
docker compose up -d
```

This will:
- Pull all required Docker images
- Create and configure the database
- Apply database migrations
- Start the application server

## 3. Verify Installation

Check if all containers are running:

```bash
docker compose ps
```

You should see all services listed as "running".

Access the application at:
```
http://localhost:8080
```

## 4. Enjoy! 🎉

You're all set. Have fun using DockiUp!

## Troubleshooting

If you encounter any issues:

1. Check container logs:
```bash
docker compose logs
```

2. Ensure all environment variables are correctly set in your `.env` file

3. Verify Docker has sufficient resources allocated

4. Check network connectivity between containers
