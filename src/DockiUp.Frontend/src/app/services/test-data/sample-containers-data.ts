import { UpdateStatusType } from "../../enums/update-status-type.enum";
import { Container } from "../../models/container.model";

export const sampleContainers: Container[] = [
    {
      id: "c1a2b3c4d5",
      name: "Backend API",
      description: "Main backend REST API service",
      gitRepo: {
        url: "https://github.com/example/backend-api.git",
        branch: "main",
        directory: "/app/backend",
        credentials: {
          username: "gituser",
          privateKey: "-----BEGIN PRIVATE KEY-----\nMIIEXAMPLEKEY\n-----END PRIVATE KEY-----"
        },
        lastCommitHash: "a1b2c3d4e5f6g7h8i9j0"
      },
      dockerContainer: {
        containerId: "abc123def456",
        imageName: "node",
        imageTag: "16-alpine",
        ports: [
          { host: 3000, container: 3000 },
          { host: 9229, container: 9229 }
        ],
        volumes: [
          { host: "/data/logs", container: "/app/logs" },
          { host: "/data/uploads", container: "/app/uploads" }
        ],
        envVars: [
          { name: "NODE_ENV", value: "production" },
          { name: "DB_HOST", value: "database.local" },
          { name: "API_KEY", value: "examplekey123" }
        ],
        networkMode: "bridge",
        restartPolicy: "unless-stopped"
      },
      updateStatus: {
        status: UpdateStatusType.UPDATED,
        message: "Container is up to date",
        lastCheckTime: new Date("2025-03-28T10:30:00Z"),
        lastSuccessfulUpdate: new Date("2025-03-25T14:15:00Z")
      },
      lastUpdated: new Date("2025-03-25T14:15:00Z"),
      autoUpdate: true,
      updateInterval: 720, // 12 hours
      buildCommand: "npm install && npm run build",
      startCommand: "npm start",
      stopCommand: "npm stop",
      createdAt: new Date("2024-11-15T08:00:00Z"),
      updatedAt: new Date("2025-03-28T10:30:00Z")
    },
    {
      id: "d6e7f8g9h0",
      name: "Frontend App",
      description: "User-facing React application",
      gitRepo: {
        url: "https://github.com/example/frontend-app.git",
        branch: "develop",
        directory: "/app/frontend",
        lastCommitHash: "j9i8h7g6f5e4d3c2b1a"
      },
      dockerContainer: {
        containerId: "def456ghi789",
        imageName: "nginx",
        imageTag: "latest",
        ports: [
          { host: 80, container: 80 },
          { host: 443, container: 443 }
        ],
        volumes: [
          { host: "/etc/nginx/conf.d", container: "/etc/nginx/conf.d" },
          { host: "/var/www/html", container: "/usr/share/nginx/html" }
        ],
        networkMode: "host",
        restartPolicy: "always"
      },
      updateStatus: {
        status: UpdateStatusType.NEEDS_UPDATE,
        message: "New commits available",
        lastCheckTime: new Date("2025-03-29T09:15:00Z"),
        lastSuccessfulUpdate: new Date("2025-03-20T11:45:00Z")
      },
      lastUpdated: new Date("2025-03-20T11:45:00Z"),
      autoUpdate: false,
      buildCommand: "npm install && npm run build",
      startCommand: "nginx -g 'daemon off;'",
      createdAt: new Date("2024-12-05T15:30:00Z"),
      updatedAt: new Date("2025-03-29T09:15:00Z")
    },
    {
      id: "i1j2k3l4m5",
      name: "Database",
      gitRepo: {
        url: "https://github.com/example/database-config.git",
        branch: "stable",
        directory: "/app/database"
      },
      dockerContainer: {
        containerId: "ghi789jkl012",
        imageName: "postgres",
        imageTag: "13",
        ports: [
          { host: 5432, container: 5432 }
        ],
        volumes: [
          { host: "/data/postgres", container: "/var/lib/postgresql/data" }
        ],
        envVars: [
          { name: "POSTGRES_USER", value: "admin" },
          { name: "POSTGRES_PASSWORD", value: "securepassword" },
          { name: "POSTGRES_DB", value: "appdb" }
        ],
        restartPolicy: "always"
      },
      updateStatus: {
        status: UpdateStatusType.FAILED,
        message: "Failed to pull latest image",
        lastCheckTime: new Date("2025-03-27T22:10:00Z"),
        lastSuccessfulUpdate: new Date("2025-02-15T18:30:00Z"),
        lastFailedUpdate: new Date("2025-03-27T22:10:00Z"),
        errorMessage: "Network timeout while pulling image"
      },
      lastUpdated: new Date("2025-02-15T18:30:00Z"),
      autoUpdate: true,
      updateInterval: 10080, // Weekly
      startCommand: "docker-entrypoint.sh postgres",
      stopCommand: "pg_ctl stop -m fast",
      createdAt: new Date("2024-09-10T12:00:00Z"),
      updatedAt: new Date("2025-03-27T22:10:00Z")
    },
    {
      id: "n6o7p8q9r0",
      name: "Redis Cache",
      description: "In-memory cache service",
      gitRepo: {
        url: "https://github.com/example/redis-config.git",
        branch: "main",
        directory: "/app/redis"
      },
      dockerContainer: {
        containerId: "jkl012mno345",
        imageName: "redis",
        imageTag: "6-alpine",
        ports: [
          { host: 6379, container: 6379 }
        ],
        volumes: [
          { host: "/data/redis", container: "/data" }
        ],
        envVars: [
          { name: "REDIS_PASSWORD", value: "redispass123" }
        ],
        networkMode: "bridge"
      },
      updateStatus: {
        status: UpdateStatusType.UPDATING,
        message: "Pulling latest image",
        lastCheckTime: new Date("2025-03-29T08:00:00Z"),
        lastSuccessfulUpdate: new Date("2025-03-10T09:45:00Z")
      },
      lastUpdated: new Date("2025-03-10T09:45:00Z"),
      autoUpdate: true,
      updateInterval: 1440, // Daily
      startCommand: "redis-server --appendonly yes",
      createdAt: new Date("2025-01-20T16:40:00Z"),
      updatedAt: new Date("2025-03-29T08:00:00Z")
    },
    {
      id: "s1t2u3v4w5",
      name: "Monitoring",
      description: "Prometheus and Grafana monitoring stack",
      gitRepo: {
        url: "https://github.com/example/monitoring.git",
        branch: "stable",
        directory: "/app/monitoring"
      },
      dockerContainer: {
        containerId: "mno345pqr678",
        imageName: "prom/prometheus",
        imageTag: "v2.35.0",
        ports: [
          { host: 9090, container: 9090 }
        ],
        volumes: [
          { host: "/etc/prometheus", container: "/etc/prometheus" },
          { host: "/data/prometheus", container: "/prometheus" }
        ],
        restartPolicy: "unless-stopped"
      },
      updateStatus: {
        status: UpdateStatusType.IDLE,
        message: "Status check pending",
        lastCheckTime: new Date("2025-03-20T14:00:00Z")
      },
      lastUpdated: new Date("2025-02-28T13:20:00Z"),
      autoUpdate: false,
      createdAt: new Date("2025-02-01T10:15:00Z"),
      updatedAt: new Date("2025-03-20T14:00:00Z")
    }
  ];