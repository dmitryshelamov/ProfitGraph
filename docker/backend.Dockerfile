# Production image of the API and the Worker (docker-compose.prod.yml, ADR 0053). CI builds it for linux/amd64 and pushes
# it to GHCR; restore goes online, unlike docker/dev-app.Dockerfile. Both hosts run from one image as the non-root app user.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP_VERSION=local
WORKDIR /src
COPY global.json ./
COPY backend ./backend
RUN dotnet restore backend/ProfitGraph.sln
RUN dotnet publish backend/src/ProfitGraph.Api/ProfitGraph.Api.csproj -c Release --no-restore -o /out/api -p:InformationalVersion=${APP_VERSION} \
    && dotnet publish backend/src/ProfitGraph.Worker/ProfitGraph.Worker.csproj -c Release --no-restore -o /out/worker -p:InformationalVersion=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_VERSION=local
ENV PROFITGRAPH_VERSION=${APP_VERSION}
WORKDIR /app
COPY --from=build /out/api ./api
COPY --from=build /out/worker ./worker
# The Data Protection key volume mounts here; a named volume takes this owner on first use.
RUN install -d -o "$APP_UID" /app/keys
USER $APP_UID
