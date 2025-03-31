# Use base ASP.NET Core runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Frontend build stage
FROM node:18 AS frontend-build
WORKDIR /src/frontend
RUN npm install -g @angular/cli@19
COPY ["src/DockiUp.Frontend/package*.json", "./"]
RUN npm ci
COPY ["src/DockiUp.Frontend", "./"]
RUN npm run build:agent --output-path /artifacts/frontend

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
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
# Fix: Path now matches the output path in the frontend-build stage
COPY --from=frontend-build /artifacts/frontend /app/publish/wwwroot

# Final production stage
FROM base AS final
WORKDIR /app
# Install git in the final stage
RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y git
COPY --from=publish /app/publish . 
ENTRYPOINT ["dotnet", "DockiUp.API.dll"]