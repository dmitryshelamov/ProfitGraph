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
docs/       архитектура, roadmap и ADR
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

Фронтенд (Node `^22.22.2 || ^24.15.0 || >=26`):

```bash
cd frontend
npm ci
npm run dev
```

Интерфейс открывается на `http://localhost:5273`; запросы `/api` проксируются в API на `localhost:5291`, поэтому
главная страница показывает состояние API. Проверки фронтенда: `npm run lint`, `npm run format:check`,
`npm run typecheck`, `npm test`, `npm run build`. Клиент API перегенерируется командой `npm run generate:api`
при запущенном API.

PostgreSQL для разработки и тестов (интеграционные тесты создают и удаляют свою базу на тестовом сервере):

```bash
docker compose -f docker-compose.dev.yml up -d --wait
docker compose -f docker-compose.test.yml up -d --wait
```

## Правила разработки

- Архитектура и правила кода: [docs/architecture.md](docs/architecture.md), решения: [docs/adr](docs/adr).
- Изменения попадают в `main` только через pull request с зелёным CI, слияние — squash.
- Инструкции для агентов: [AGENTS.md](AGENTS.md).
