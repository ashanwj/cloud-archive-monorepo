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

# Stage 2 — Runtime (lean ASP.NET image, no SDK)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for container security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build /app/publish .

# App Runner routes traffic to port 8080
EXPOSE 8080

USER appuser

ENTRYPOINT ["dotnet", "CloudArchive.Api.dll"]
