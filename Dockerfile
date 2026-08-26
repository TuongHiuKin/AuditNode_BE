# =============================================================================
# Stage 1: Build & Publish
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./

# ── Restore layer (copy only .csproj files first for layer caching) ──────────
COPY AuditNode.Domain/AuditNode.Domain.csproj             AuditNode.Domain/
COPY AuditNode.Domain/packages.lock.json                  AuditNode.Domain/
COPY AuditNode.Application/AuditNode.Application.csproj   AuditNode.Application/
COPY AuditNode.Application/packages.lock.json             AuditNode.Application/
COPY AuditNode.Infrastructure/AuditNode.Infrastructure.csproj AuditNode.Infrastructure/
COPY AuditNode.Infrastructure/packages.lock.json          AuditNode.Infrastructure/
COPY AuditNode.API/AuditNode.API.csproj                   AuditNode.API/
COPY AuditNode.API/packages.lock.json                     AuditNode.API/

RUN dotnet restore AuditNode.API/AuditNode.API.csproj --locked-mode

# ── Copy the rest of the source and publish ───────────────────────────────────
COPY . .

RUN dotnet publish AuditNode.API/AuditNode.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# =============================================================================
# Stage 2: Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Expose application port
EXPOSE 5000

# Tell ASP.NET Core which URL to bind
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "AuditNode.API.dll"]
