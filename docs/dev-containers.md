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
- The connection string (`host.docker.internal:5435`) is an environment variable of this compose file only. Steam and
  T-Invest are enabled by `appsettings.Development.json`, as for a host run; `Live:Trading:Enabled` stays `false` in
  every committed file.
- `DataProtection__KeyDirectory=/root/.aspnet/DataProtection-Keys` is a named volume. Setting the key directory also
  makes `AuthenticationSetup` pin the application name `ProfitGraph`; without it the name derives from the content
  root path, which would change keys between builds.
- Development user secrets (`~/.microsoft/usersecrets/profitgraph-{api,worker}-development`) hold the real values of
  `Secrets:*`, `Auth:BootstrapOwner:*` and the Worker's `Live:Trading:Enabled`, whose committed values are empty
  placeholders or `false`, and may repeat other settings. They are bind-mounted read-only.
  Their content is never copied into the image or the repository; a missing directory fails `up` instead of being
  created empty.

## T-Invest in the scanner

The development Worker runs the `scanner` role. T-Invest joins it ([ADR 0062](adr/0062-tinvest-market-scanner.md)) once
both user secrets name a stored T-Invest credential, for example a read-only token added on the credentials page:

```bash
dotnet user-secrets set "Venues:TInvest:Scanner:CredentialId" "<credential id>" --project backend/src/ProfitGraph.Api
dotnet user-secrets set "Venues:TInvest:Scanner:CredentialId" "<credential id>" --project backend/src/ProfitGraph.Worker
```

Re-create the containers afterwards. The scanner then reads the real Invest API with that token; do it only on the owner's
instruction.

## Do not touch

The development database on `5435` is persistent; do not drop, clean or edit it by hand. The Profit Fox Designer stack
(ports 5173, 5191, 5433, 5434) is separate and must stay as it is. Live trading is disabled in every committed
configuration, and this setup does not change that.
