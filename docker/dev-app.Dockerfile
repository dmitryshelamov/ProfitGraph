# Dev image for the API and the Worker (docker-compose.dev-app.yml). Sources are copied in and built inside the image, so the
# host working tree and its macOS bin/obj stay untouched. Packages are restored offline from the host NuGet cache, passed as
# the named build context "nugetcache" (api.nuget.org may be unreachable from the host network, so restore never goes online).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY backend ./backend
RUN --mount=type=bind,from=nugetcache,target=/nuget-offline \
    dotnet restore backend/ProfitGraph.sln --source /nuget-offline \
    && dotnet publish backend/src/ProfitGraph.Api/ProfitGraph.Api.csproj -c Release --no-restore -o /out/api \
    && dotnet publish backend/src/ProfitGraph.Worker/ProfitGraph.Worker.csproj -c Release --no-restore -o /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/api ./api
COPY --from=build /out/worker ./worker
