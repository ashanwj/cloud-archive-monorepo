# Stage 1 — Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first for layer caching (restore is slow)
COPY CloudArchive.Api.csproj ./
RUN dotnet restore CloudArchive.Api.csproj

# Copy source and publish
COPY . .
RUN dotnet publish CloudArchive.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# Stage 2 — Runtime (chiseled Ubuntu: minimal attack surface, built-in non-root 'app' user)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# App Runner routes traffic to port 8080
EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "CloudArchive.Api.dll"]
