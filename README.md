# ProfitGraph

Визуальный конструктор торговых стратегий с воспроизводимым бэктестингом, оптимизацией параметров и live-ботами.

Стратегия собирается из блоков (события, условия, ордера, отмены) и связей между ними. Один и тот же движок графа
исполняет её на исторических данных и в live-боте, поэтому результат бэктеста детерминирован, а поведение бота
совпадает с проверенным в бэктесте.

ProfitGraph — пересборка проекта Profit Fox Designer: код переносится модулями в новую архитектуру
с учётом ошибок MVP. План и порядок переноса — в [docs/roadmap.md](docs/roadmap.md).

## Структура

```text
backend/    .NET 10: Engine, Domain, Application, Infrastructure, Api, Worker и модули площадок
frontend/   React 19, TypeScript, Vite: веб-интерфейс
docs/       архитектура, roadmap, ADR и runbook прода (docs/production.md)
deploy/     скрипт деплоя и примеры настроек прода
docker/     Dockerfile образов и Caddyfile
```

## Запуск и проверки

```bash
dotnet build backend/ProfitGraph.sln
dotnet test backend/ProfitGraph.sln
```

API в режиме разработки:

```bash
dotnet run --project backend/src/ProfitGraph.Api --launch-profile http
```

Проверка: `curl http://localhost:5291/api/health` возвращает `Healthy`.

Фоновые задачи (загрузка истории, прогоны) выполняет Worker; запустите его рядом с API:

```bash
dotnet run --project backend/src/ProfitGraph.Worker
```

Без Worker задачи остаются в очереди, а `/api/health/ready` отвечает `Degraded`.

Все остальные endpoint требуют входа. Первый Owner создаётся из приватной конфигурации, пока в базе нет ни одного
аккаунта. Для разработки задайте его через user-secrets (пароль не короче 12 символов, в репозиторий не попадает):

```bash
dotnet user-secrets --project backend/src/ProfitGraph.Api set "Auth:BootstrapOwner:Login" "owner"
```

```bash
dotnet user-secrets --project backend/src/ProfitGraph.Api set "Auth:BootstrapOwner:Password" "<пароль>"
```

Ключи бирж хранятся зашифрованными мастер-ключом (ADR 0015). Без него endpoint ключей отвечают `503`. Для разработки
сгенерируйте случайный 32-байтный ключ и сохраните его только в user-secrets API:

```bash
dotnet user-secrets --project backend/src/ProfitGraph.Api set "Secrets:MasterKeys:dev-1" "$(openssl rand -base64 32)"
```

```bash
dotnet user-secrets --project backend/src/ProfitGraph.Api set "Secrets:ActiveKeyId" "dev-1"
```

Фронтенд (Node `^22.22.2 || ^24.15.0 || >=26`):

```bash
cd frontend
npm ci
npm run dev
```

Интерфейс открывается на `http://localhost:5273`; запросы `/api` проксируются в API на `localhost:5291`. Без входа
открывается страница входа: войдите учётной записью Owner из user-secrets, после входа откроется список стратегий.
Проверки фронтенда: `npm run lint`, `npm run format:check`,
`npm run typecheck`, `npm test`, `npm run build`. Клиент API перегенерируется командой `npm run generate:api`
при запущенном API.

Сквозные тесты редактора (Playwright) запускаются при работающем тестовом PostgreSQL:

```bash
cd frontend
npx playwright install chromium
npm run test:e2e
```

Playwright сам поднимает API и фронтенд на отдельных портах, создаёт одноразовую базу на тестовом сервере (5436)
с владельцем на случайном пароле и удаляет её после прогона.

PostgreSQL для разработки и тестов (интеграционные тесты создают и удаляют свою базу на тестовом сервере):

```bash
docker compose -f docker-compose.dev.yml up -d --wait
docker compose -f docker-compose.test.yml up -d --wait
```

На macOS .NET не проходит TLS-рукопожатие с T-Invest, поэтому API и Worker для работы с T-Invest запускаются в Linux-контейнерах
(NuGet восстанавливается офлайн из кэша хоста, `bin/` и `obj/` хоста не затрагиваются):

```bash
docker compose -f docker-compose.dev-app.yml up -d --build --wait
docker compose -f docker-compose.dev-app.yml down
```

Подробности: [docs/dev-containers.md](docs/dev-containers.md).

## Правила разработки

- Архитектура и правила кода: [docs/architecture.md](docs/architecture.md), решения: [docs/adr](docs/adr).
- Изменения попадают в `main` только через pull request с зелёным CI, слияние — squash.
- Инструкции для агентов: [AGENTS.md](AGENTS.md).

## Прод

Прод работает на `192.168.8.111` в Docker Compose за Caddy с HTTPS. Merge в `main` собирает образы в GHCR, а деплой
запускается после подтверждения окружения `production` в GitHub Actions. Порядок работы и первичная настройка описаны в
`docs/production.md`.
