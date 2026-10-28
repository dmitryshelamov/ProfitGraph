# Production runbook

How ProfitGraph runs in production and how to deploy it ([ADR 0053](adr/0053-production-deployment.md)).

## Layout

| What | Where |
|---|---|
| Host | `192.168.8.111` (`soyo-01`, Ubuntu 26.04), user `soyo` in the `docker` group, SSH by key |
| Address | `https://192.168.8.111` (Caddy, certificate from its local CA) |
| Stack | Compose project `profitgraph-production` in `~/profitgraph`: `postgres`, `migrate`, `api`, `worker`, `web` |
| Settings | `~/profitgraph/.env` and `~/profitgraph/master-keys.env`, both `600`, never in the repository |
| Deployed version | `~/profitgraph/deployed-version` |
| Images | `ghcr.io/dmitryshelamov/profitgraph-backend` and `profitgraph-web`, tags `1.0.<run number>` |
| Deploy runner | `~/actions-runner`, label `profitgraph-production`, a systemd service |
| Volumes | `postgres_data`, `api_keys` (Data Protection), `caddy_data` (local CA and certificates), `caddy_config` |

## Deploy

1. Merge the pull request. The `Production` workflow builds the images for that commit.
2. Open the run in GitHub Actions, then **Review deployments** → `production` → **Approve and deploy**.
3. The `deploy` job runs `deploy/deploy.sh <version>` on the host and passes once `/api/health/ready` reports `Healthy`.

Redeploy or roll back: **Actions → Production → Run workflow** with the version, for example `1.0.42`. An empty version
builds and deploys the current `main`. A rollback does not undo migrations; roll back only to a version that works
with the current schema.

By hand on the host, with the repository checked out at the version's commit:

```bash
deploy/deploy.sh 1.0.42
```

`deploy.sh` needs `docker login ghcr.io` while the packages are private.

## Operate

```bash
ssh 192.168.8.111
cd ~/profitgraph
export PROFITGRAPH_VERSION=$(cat deployed-version)
docker compose ps
docker compose logs --tail 200 worker
docker compose restart worker
```

Every `docker compose` command in `~/profitgraph` needs `PROFITGRAPH_VERSION` set as above. Never pass `-v` to
`docker compose down`: it deletes the database, the sign-in keys and the local CA.

`/api/health` is liveness; `/api/health/ready` is `Healthy` with the database and a beating Worker, `Degraded` without
the Worker.

## Switches

`.env` turns on what is off by default; after a change, run `docker compose up -d api worker` in `~/profitgraph`:

- `LIVE_TRADING_ENABLED=true` lets the Worker send real orders. Only on the owner's instruction.
- `TELEGRAM_ENABLED=true` with `TELEGRAM_BOT_TOKEN` and `TELEGRAM_BOT_USERNAME`.
- `STEAM_ENABLED=true` with `STEAM_MARKET_DATA_CREDENTIAL_ID` once the Steam credential exists
  ([Steam owner checklist](steam-owner-checklist.md)).
- `TINVEST_ENABLED=true`.

## First setup

Done once per host; the steps that need `sudo` are the owner's.

1. Docker Engine with Compose, and `soyo` in the `docker` group.
2. `~/profitgraph` (`700`) with `.env` from [`deploy/production.env.example`](../deploy/production.env.example) and
   `master-keys.env` from [`deploy/master-keys.env.example`](../deploy/master-keys.env.example), both `600`. Keep an
   offline copy of `master-keys.env`: without it, stored credentials cannot be decrypted.
3. The runner. Register it with a token from **Settings → Actions → Runners → New self-hosted runner** (or
   `gh api -X POST repos/dmitryshelamov/ProfitGraph/actions/runners/registration-token`):

   ```bash
   mkdir ~/actions-runner && cd ~/actions-runner
   # download and unpack the Linux x64 runner from that page, then:
   ./config.sh --unattended --url https://github.com/dmitryshelamov/ProfitGraph --token <token> \
       --name soyo-01 --labels profitgraph-production --work _work
   sudo ./svc.sh install soyo && sudo ./svc.sh start
   ```

4. GitHub settings: the `production` environment with the owner as required reviewer and deployments from `main` only;
   **Settings → Actions → General → Approval for running fork pull request workflows** set to all outside collaborators.
5. The first deployment through the workflow. The Bootstrap Owner from `.env` is created on the empty database; sign in,
   then clear `BOOTSTRAP_OWNER_PASSWORD` in `.env`.
6. Trust Caddy's root certificate on each device that opens the site:

   ```bash
   scp 192.168.8.111:~/profitgraph/caddy-root.crt .
   ```

   after copying it out on the host with `docker compose cp web:/data/caddy/pki/authorities/local/root.crt caddy-root.crt`.
   On macOS: open it in Keychain Access, add it to System and set it to Always Trust.

## Rotate the master key

Add a new line `Secrets__MasterKeys__<new-id>=<key>` to `master-keys.env`, set `Secrets__ActiveKeyId=<new-id>`, keep the
old line, and run `docker compose up -d api worker`. New values are encrypted with the new key; old ones still decrypt
with the old key.
