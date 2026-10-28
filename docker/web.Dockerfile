# Production web image (docker-compose.prod.yml, ADR 0053): the built frontend served by Caddy, which terminates HTTPS and
# proxies /api and /ws to the API. CI builds it for linux/amd64 and pushes it to GHCR.
FROM node:24-alpine AS build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend ./
RUN npm run build

FROM caddy:2.11-alpine
COPY docker/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/frontend/dist /srv
