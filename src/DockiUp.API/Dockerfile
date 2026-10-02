# Use base ASP.NET Core runtime (.NET 10)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stages run on the build machine's own architecture ($BUILDPLATFORM): the Angular bundle and the
# framework-dependent .NET output are architecture-independent, so a multi-arch release only emulates the
# final stage instead of compiling everything under QEMU.

# Frontend build stage (DockiUp.Frontend - in-repo at src/DockiUp.Frontend)
FROM --platform=$BUILDPLATFORM node:22 AS frontend-build
WORKDIR /src/frontend
COPY src/DockiUp.Frontend/package*.json ./
COPY src/DockiUp.Frontend/ ./
RUN npm install
RUN npx ng build --configuration=production 2>/dev/null || npm run build -- --configuration=production
RUN mkdir -p /artifacts/frontend && (cp -r dist/DockiUp.Frontend/browser/* /artifacts/frontend/ 2>/dev/null || cp -r dist/*/browser/* /artifacts/frontend/ 2>/dev/null || cp -r dist/* /artifacts/frontend/ || true)

# Backend build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/DockiUp.API/DockiUp.API.csproj", "src/DockiUp.API/"]
COPY ["src/DockiUp.Application/DockiUp.Application.csproj", "src/DockiUp.Application/"]
COPY ["src/DockiUp.Infrastructure/DockiUp.Infrastructure.csproj", "src/DockiUp.Infrastructure/"]
COPY ["src/DockiUp.Domain/DockiUp.Domain.csproj", "src/DockiUp.Domain/"]
RUN dotnet restore "src/DockiUp.API/DockiUp.API.csproj"
COPY . .
WORKDIR "/src/src/DockiUp.API"
RUN dotnet build "DockiUp.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DockiUp.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
COPY --from=frontend-build /artifacts/frontend /app/publish/wwwroot

# Final production stage
FROM base AS final
WORKDIR /app

# Run as root so the mounted /var/run/docker.sock is accessible out of the box on every
# host (Docker Desktop exposes it root:root 0660; Linux exposes it root:docker 0660 with a
# host-specific GID). DockiUp needs full Docker access to manage compose projects; dropping
# privilege while holding the socket gains nothing, since socket access is root-equivalent
# on the host either way. Override with `user: "<uid>:<gid>"` in compose if you have a
# different threat model and a known GID to map.
USER root

# git: clone project repos. docker CLI + compose plugin: DockerService shells out to
# `docker compose` for project start/stop/restart against the mounted socket.
RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y --no-install-recommends git && \
    rm -rf /var/lib/apt/lists/*
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker:27-cli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DockiUp.API.dll"]
