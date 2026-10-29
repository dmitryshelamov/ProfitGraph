#!/usr/bin/env bash
# Deploys one ProfitGraph version on the production host (ADR 0053, docs/production.md): pulls its images, applies
# migrations, restarts the API, the Worker and Caddy on it and waits until /api/health/ready reports Healthy.
# The production deploy job runs it on the host's self-hosted runner; the owner can run it on the host by hand.
# Usage: deploy/deploy.sh <version>, for example deploy/deploy.sh 1.0.42.
set -euo pipefail

version="${1:?Usage: deploy.sh <version>}"
home="${PROFITGRAPH_HOME:-$HOME/profitgraph}"
source_dir="$(cd "$(dirname "$0")/.." && pwd)"

for file in .env master-keys.env; do
    if [[ ! -f "$home/$file" ]]; then
        echo "$home/$file is missing; create it from deploy/$( [[ $file == .env ]] && echo production.env.example || echo master-keys.env.example )." >&2
        exit 1
    fi
done

install -m 0644 "$source_dir/docker-compose.prod.yml" "$home/docker-compose.yml"
cd "$home"
export PROFITGRAPH_VERSION="$version"

echo "Pulling $version"
docker compose pull --quiet api worker worker-scanner web

# PostgreSQL's tag does not change between versions, and Docker Hub's token service is not always reachable from the host:
# a present image is kept when the pull fails, a missing one still fails the deployment.
if ! docker compose pull --quiet postgres; then
    docker image inspect "$(docker compose config --images postgres)" > /dev/null || exit 1
    echo "Docker Hub is unreachable; keeping the present PostgreSQL image." >&2
fi

docker compose --profile migrate pull --quiet migrate

echo "Applying migrations"
docker compose up --detach --wait postgres
docker compose --profile migrate run --rm migrate

echo "Starting $version"
docker compose up --detach --wait --remove-orphans api worker worker-scanner web

echo "Waiting for the Worker"
status=""

for _ in $(seq 1 60); do
    status="$(docker compose exec -T api bash -c \
        "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /api/health/ready HTTP/1.0\r\n\r\n' >&3 && tail -n1 <&3" 2>/dev/null || true)"

    if [[ "$status" == "Healthy" ]]; then
        break
    fi

    sleep 2
done

if [[ "$status" != "Healthy" ]]; then
    echo "/api/health/ready reports '${status:-no answer}' two minutes after the start of $version." >&2
    docker compose ps >&2
    exit 1
fi

echo "$version" > deployed-version
docker image prune --force > /dev/null
echo "Deployed $version"
