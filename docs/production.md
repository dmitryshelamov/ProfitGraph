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
| Deploy runner | `192.168.8.142` (`soyo-03`): `~/actions-runner`, label `profitgraph-deploy`, user service `github-runner-profitgraph` |
| Deploy key | `~/.ssh/id_ed25519_profitgraph_deploy` on `192.168.8.142`, SSH alias `profitgraph-production`; the host accepts it only from `192.168.8.142` |
| Volumes | `postgres_data`, `api_keys` (Data Protection), `caddy_data` (local CA and certificates), `caddy_config` |

## Deploy

1. Merge the pull request. The `Production` workflow builds the images for that commit.
2. Open the run in GitHub Actions, then **Review deployments** → `production` → **Approve and deploy**.
3. The `deploy` job, on the runner of `192.168.8.142`, copies the compose file and `deploy.sh` to
   `~/profitgraph/release` on the host over SSH, signs the host in to GHCR for the run, runs
   `deploy.sh <version>` there and passes once `/api/health/ready` reports `Healthy`.

Redeploy or roll back: **Actions → Production → Run workflow** with the version, for example `1.0.42`. An empty version
builds and deploys the current `main`. A rollback does not undo migrations; roll back only to a version that works
with the current schema.

By hand on the host, with the files of the last deployment:

```bash
docker login ghcr.io
~/profitgraph/release/deploy/deploy.sh 1.0.42
docker logout ghcr.io
```

`docker login` takes a GitHub token with `read:packages` while the packages are private.

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

Done once; the steps that need `sudo` are the owner's. The production host runs production only; the runner lives on
`192.168.8.142`.

1. On the host: Docker Engine with Compose, and `soyo` in the `docker` group.
2. On the host: `~/profitgraph` (`700`) with `.env` from
   [`deploy/production.env.example`](../deploy/production.env.example) and `master-keys.env` from
   [`deploy/master-keys.env.example`](../deploy/master-keys.env.example), both `600`. Keep an offline copy of
   `master-keys.env`: without it, stored credentials cannot be decrypted.
3. On `192.168.8.142`, the runner. Register it with a token from **Settings → Actions → Runners → New self-hosted
   runner** (or `gh api -X POST repos/dmitryshelamov/ProfitGraph/actions/runners/registration-token`):

   ```bash
   mkdir ~/actions-runner && cd ~/actions-runner
   # download and unpack the Linux x64 runner from that page, then:
   ./config.sh --unattended --url https://github.com/dmitryshelamov/ProfitGraph --token <token> \
       --name soyo-03 --labels profitgraph-deploy --work _work
   ```

   It runs as the user service `~/.config/systemd/user/github-runner-profitgraph.service` (`ExecStart` is
   `~/actions-runner/run.sh`, `Restart=always`), enabled with `systemctl --user enable --now github-runner-profitgraph`;
   `loginctl enable-linger soyo` keeps it running without a session and starts it at boot.
4. On `192.168.8.142`, the deploy key `~/.ssh/id_ed25519_profitgraph_deploy` and an SSH alias in `~/.ssh/config`:

   ```text
   Host profitgraph-production
       HostName 192.168.8.111
       User soyo
       IdentityFile ~/.ssh/id_ed25519_profitgraph_deploy
       IdentitiesOnly yes
       BatchMode yes
   ```

   with the host's key in `~/.ssh/known_hosts`, checked against its fingerprint. On the host, its public key in
   `~/.ssh/authorized_keys` behind `from="192.168.8.142",no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty`.
5. GitHub settings: the `production` environment with the owner as required reviewer and deployments from `main` only;
   **Settings → Actions → General → Approval for running fork pull request workflows from contributors** set to **Require approval for all external contributors**.
6. The first deployment through the workflow. The Bootstrap Owner from `.env` is created on the empty database; sign in,
   then clear `BOOTSTRAP_OWNER_PASSWORD` in `.env`.
7. Trust Caddy's root certificate on each device that opens the site:

   ```bash
   scp 192.168.8.111:~/profitgraph/caddy-root.crt .
   ```

   after copying it out on the host with `docker compose cp web:/data/caddy/pki/authorities/local/root.crt caddy-root.crt`.
   On macOS: open it in Keychain Access, add it to System and set it to Always Trust.

## Rotate the master key

Add a new line `Secrets__MasterKeys__<new-id>=<key>` to `master-keys.env`, set `Secrets__ActiveKeyId=<new-id>`, keep the
old line, and run `docker compose up -d api worker`. New values are encrypted with the new key; old ones still decrypt
with the old key.
