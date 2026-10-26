# Running the API and Worker in containers (macOS and T-Invest)

On macOS the locally run API and Worker cannot reach T-Invest: the .NET TLS handshake with `invest-public-api.tbank.ru`
fails with `AuthenticationException` ("bad certificate format") before `TInvestCertificateValidator` is called, so every
instrument-rules request ends as `instrument_rules_provider_unavailable`. The cause is not proven; a suspect is the
parsing of a certificate whose Subject carries `1.2.643.*` OIDs. The same code works in a Linux container, so
`docker-compose.dev-app.yml` runs the API and the Worker there. Everything else (tests, frontend, other venues) keeps
running on the host as usual. On Linux hosts the plain `dotnet run` is enough.

## Start

```bash
docker compose -f docker-compose.dev.yml up -d --wait
docker compose -f docker-compose.dev-app.yml up -d --build --wait
cd frontend && npm run dev
```

The API listens on `127.0.0.1:5291` (override with `PROFITGRAPH_API_PORT`), and the Vite proxy already targets that
address (`PROFITGRAPH_API` changes it). Stop any host-run API on 5291 first. Check `http://localhost:5291/api/health/ready`:
`Healthy` means the Worker is up too.

## Stop

```bash
docker compose -f docker-compose.dev-app.yml down
```

`down` keeps the Data Protection volume, so cookie sign-in survives re-creating the containers. Do not pass `-v`.

## How it is built

- `docker/dev-app.Dockerfile` copies `backend/` into a build stage and publishes the API and the Worker inside the image,
  so the host `bin/` and `obj/` (macOS build) are never written.
- NuGet restore is offline from the host cache (`~/.nuget/packages`, or `NUGET_PACKAGES`), passed as the build context
  `nugetcache`. In the environment this was written in, `api.nuget.org` itself timed out from the host, which is why
  an online restore inside the container hung. Run `dotnet restore backend/ProfitGraph.sln` on the host once (or after
  a package change) so the cache is complete; a missing package fails the build with a restore error.
- The connection string (`host.docker.internal:5435`), `Venues__TInvest__Enabled=true` and
  `Venues__TInvest__Endpoint` are environment variables of this compose file only. Committed `appsettings*.json` keep
  T-Invest off, and `Live:Trading:Enabled` stays `false`.
- `DataProtection__KeyDirectory=/root/.aspnet/DataProtection-Keys` is a named volume. Setting the key directory also
  makes `AuthenticationSetup` pin the application name `ProfitGraph`; without it the name derives from the content
  root path, which would change keys between builds.
- Development user secrets (`~/.microsoft/usersecrets/profitgraph-{api,worker}-development`) are bind-mounted read-only.
  Their content is never copied into the image or the repository; a missing directory fails `up` instead of being
  created empty.

## Do not touch

The development database on `5435` is persistent; do not drop, clean or edit it by hand. The Profit Fox Designer stack
(ports 5173, 5191, 5433, 5434) is separate and must stay as it is. Live trading is disabled in every committed
configuration, and this setup does not change that.
