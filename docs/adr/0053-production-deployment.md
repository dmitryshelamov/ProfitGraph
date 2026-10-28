# ADR 0053: Production Deployment

Status: Accepted, 2026-10-29. Stage 6 of the roadmap, first part: images, the production stack and the deploy job. The
watchdog, backups and the switch-over from Profit Fox follow in their own pull requests. Proven by a local run of
`deploy/deploy.sh` against locally built images; the first deployment to the host is still to come.

## Context

ProfitGraph had only development stacks: the development and test PostgreSQL and, for T-Invest on macOS, the API and the
Worker in a development container built from the host's NuGet cache. Production needs packaged processes, ordered
migrations, HTTPS for the Secure sign-in cookie and a repeatable way to put a version on the host.

The host is a single Ubuntu 26.04 machine on the owner's local network, `192.168.8.111` (`soyo-01`), with Docker Engine
and Compose. It has no public address (ADR 0042). From that network `api.nuget.org` does not answer, while GitHub and
GHCR do; the owner's Mac is arm64 and the host amd64.

Profit Fox built its image after the tests on the default branch, pushed it to the GitLab registry as
`1.0.<pipeline number>` and deployed it with a manual job that reached the host over SSH with a password. Profit Fox
Designer (archive ADR 0060) settled the stack: PostgreSQL without a published port, a one-shot migration, an API and a
live process, secrets from the environment, trading and Telegram off by default.

## Decision

- **Images.** Two images for `linux/amd64`, built on GitHub-hosted runners and pushed to GHCR as
  `profitgraph-backend:1.0.<run number>` and `profitgraph-web:1.0.<run number>`:
  - `docker/backend.Dockerfile` publishes the API and the Worker into one ASP.NET runtime image; they run as its
    non-root `app` user. The version is the assembly's informational version and `PROFITGRAPH_VERSION`.
  - `docker/web.Dockerfile` builds the frontend and serves it with Caddy (`docker/Caddyfile`).
- **Stack.** `docker-compose.prod.yml`, project `profitgraph-production`:
  - `postgres` (17, named volume, no published port);
  - `migrate`, the backend image with `--migrate-only`, in the `migrate` profile, so that `up` never starts it;
  - `api` with Data Protection keys in a named volume and the Bootstrap Owner settings;
  - `worker` with both roles and `Live:Trading:Enabled` from `.env`, `false` by default;
  - `web`, Caddy on ports 80 and 443 with a fixed address on the stack's network.
  Every service restarts unless stopped and rotates its JSON log at 10 MB × 3.
- **HTTPS.** Caddy serves `https://<PROFITGRAPH_HOST>` with a certificate from its own local CA (`tls internal`), kept in
  the `caddy_data` volume, and redirects HTTP. The owner trusts that CA's root certificate on their devices once. Caddy
  serves the frontend with an `index.html` fallback and proxies `/api` and `/ws`. The API trusts `X-Forwarded-For` and
  `X-Forwarded-Proto` only from Caddy's fixed address (`ReverseProxy:KnownProxies`).
- **Settings and secrets.** No secret is in the repository or the images:
  - `.env` on the host, from `deploy/production.env.example`, holds the address, the database password, the Bootstrap
    Owner and the switches for trading, Telegram, Steam and T-Invest;
  - `master-keys.env`, from `deploy/master-keys.env.example`, holds the `Secrets` section in .NET variable names, so
    that rotation can list more than one key.
  Both files are `600`. Venue addresses are fixed in the compose file; the venues are off unless `.env` turns them on.
  The connection string disables GSS encryption, which the image has no library for.
- **Deployment.** `deploy/deploy.sh <version>`, on the host:
  1. copies the compose file to `~/profitgraph`;
  2. pulls the version;
  3. starts PostgreSQL and runs `migrate`;
  4. starts `api`, `worker` and `web` on the new version;
  5. waits until `/api/health/ready` reports `Healthy`, that is until the Worker beats too.
  The previous version keeps running while the migrations apply, so a migration must not break it.
- **Deploy job.** `.github/workflows/production.yml`:
  - On a push to `main` it builds and pushes the images.
  - Its `deploy` job waits for the owner's approval in the `production` environment. One deployment runs at a time.
  - The job runs on a self-hosted runner with the label `profitgraph-deploy` on another machine of the local network,
    `192.168.8.142` (`soyo-03`): GitHub-hosted runners cannot reach the host, and the host runs production only. Over
    SSH with a deploy key it copies the compose file and `deploy.sh` to `~/profitgraph/release`, signs the host in to
    GHCR with the run's `GITHUB_TOKEN` (so the packages may stay private), runs `deploy.sh` and signs out.
  - The host accepts the deploy key only from `192.168.8.142`, without forwarding or a terminal.
  - A manual run with a version redeploys or rolls back to that version; with no version it builds and deploys `main`.
  - Pull requests that touch the deployment build the images without pushing them.
  The repository is public, so pull requests from outside collaborators need approval before any workflow runs;
  otherwise one could run code on the runner.
- **Migrations.** The first production deployment ends ADR 0006's rule of one initial migration per feature edited in
  place. From then on, a schema change is a new migration.

## Consequences

- A merge to `main` puts nothing on the host until the owner approves; a rollback is a manual run with an older version.
- Each deployment restarts the Worker, so running bots stop for up to the 45 s grace period and resume from their
  checkpoints.
- The Data Protection keys sit unencrypted in their volume (the API logs a warning); the volume and the host are the
  protection, as in development.
- Backups of `postgres_data`, `api_keys` and `master-keys.env` do not exist yet; the backup pull request adds them.
  Until then, the master key's offline copy is the owner's.
