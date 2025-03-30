# Use base ASP.NET Core runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DockiUp.API.csproj", "."]
RUN dotnet restore "./DockiUp.API.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./DockiUp.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DockiUp.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final production stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ConnectionStrings__DockiUpDatabase="Server=${DB_SERVER};Port=${DB_PORT};Database=${DB_NAME};User=${DB_USER};Password=${DB_PASSWORD};"
ENV SystemPaths__DockerSocket=${DOCKER_SOCKET_PATH}
ENV SystemPaths__ProjectsPath=${PROJECTS_PATH}
ENTRYPOINT ["dotnet", "DockiUp.API.dll"]
