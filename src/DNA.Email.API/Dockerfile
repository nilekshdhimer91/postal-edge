# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer-cached restore
COPY Directory.Packages.props .
COPY Directory.Build.props .
COPY src/DNA.Email.Core/DNA.Email.Core.csproj src/DNA.Email.Core/
COPY src/DNA.Email.API/DNA.Email.API.csproj src/DNA.Email.API/

RUN dotnet restore src/DNA.Email.API/DNA.Email.API.csproj

COPY . .

RUN dotnet publish src/DNA.Email.API/DNA.Email.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd -g 1001 appgroup && \
    useradd -u 1001 -g appgroup -s /bin/false -M appuser

RUN mkdir -p /app/certs && chown appuser:appgroup /app/certs

COPY --from=build --chown=appuser:appgroup /app/publish .

USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "DNA.Email.API.dll"]
