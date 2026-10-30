# 1. Frontend (React + Vite)
FROM mirror.gcr.io/library/node:26-alpine AS front
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# 2. Backend (.NET)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS back
ARG VERSION=0.0.0-dev
WORKDIR /src/backend
COPY backend/ ./
RUN dotnet publish src/Noosphere.Api/Noosphere.Api.csproj -c Release -o /app -p:Version=$VERSION

# 3. Runtime image, non-root
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=back /app ./
COPY --from=front /src/frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    NOOSPHERE_KEYS_DIR=/data/keys \
    NOOSPHERE_BACKUPS_DIR=/data/backups
EXPOSE 8080
# Session keys and pre-migration backups (volumes), owned by the image's non-root user.
RUN mkdir -p /data/keys /data/backups && chown $APP_UID /data/keys /data/backups
# Command line: "docker compose exec noosphere noosphere help". Without arguments it prints the help.
RUN printf '#!/bin/sh\n[ $# -eq 0 ] && set -- help\nexec dotnet /app/Noosphere.dll "$@"\n' > /usr/local/bin/noosphere && chmod 755 /usr/local/bin/noosphere
USER $APP_UID
# The runtime image has no curl: the application probes its own /health/ready.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD ["dotnet", "/app/Noosphere.dll", "health"]
ENTRYPOINT ["dotnet", "/app/Noosphere.dll"]
