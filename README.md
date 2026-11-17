# SecuritiesRepository

Демонстрационная система учёта позиций и операций инвестиционных фондов для технического собеседования Senior .NET Developer. Все данные вымышленные. Это модульный монолит с отдельным процессом фоновой доставки событий, а не нормативная система специализированного депозитария.

## Реализовано

- .NET SDK **8.0.428**, ASP.NET Core 8, EF Core 8, Npgsql, PostgreSQL 16.
- Credit, Debit, Transfer и корректирующая Reverse с неизменяемым журналом.
- Идемпотентность команды, атомарные остатки, блокировки PostgreSQL, запрет отрицательных позиций.
- Transactional Outbox, RabbitMQ и MassTransit, идемпотентный Inbox в Worker.
- Роли Admin / Operator / Auditor, JWT, Swagger, Problem Details.
- Ручная сверка полного контрольного набора; упрощённая историческая оценка по журналу.
- React 18, TypeScript, Vite; восемь экранов операциониста с реальным API.
- xUnit, Testcontainers PostgreSQL, Docker Compose, GitHub Actions без публикации контейнеров.

## Архитектура

`Domain` содержит сущности, правила и план проводок. `Application` — команды и DTO. `Infrastructure` — EF, транзакционные сервисы и доставка сообщений. `Api` — HTTP, авторизация и композиция. `Worker` — единственный тип фонового процесса, использует ту же БД. `Contracts` — контракт события. Прямое использование небольшого DbContext в query endpoints сознательно заменяет ненужные repository-обёртки.

Подробности: [модель](docs/domain-model.md), [архитектура](docs/architecture.md), [SQL](docs/schema.sql), [вопросы для интервью](docs/interview-guide.md), [ADR](docs/adr/001-accounting.md).

## Быстрый запуск с Docker

Из корня проекта в PowerShell:

```powershell
Copy-Item deploy/.env.example deploy/.env
# Задайте свои локальные пароли и случайный JWT_KEY длиной не менее 32 байт.
notepad deploy/.env
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up --build -d
```

- Интерфейс: http://localhost:3000
- Swagger: http://localhost:8080/swagger
- API health: http://localhost:8080/api/health
- RabbitMQ management: http://localhost:15672 (пользователь `demo`, пароль из `.env`).

Compose запускается в **Development**, порты привязаны к loopback. Это локальный демонстрационный контур. Одноразовый `init` создаёт схему и вымышленные справочники, а затем проводит начальное зачисление 100 акций. API не зависит от доступности RabbitMQ. Данные PostgreSQL и RabbitMQ сохраняются в volumes. Повторный init не дублирует начальное зачисление и восстанавливает его после прерванного seed. Параллельные init сериализуются отдельной блокировкой; существующие пользовательские данные не перезаписываются.

В интерфейсе выберите роль и нажмите «Войти с тестовой ролью». В Development endpoint выдачи токена намеренно открыт. В другом окружении его маршрут отсутствует. Для production требуется внешний доверенный процесс выдачи JWT, ротация ключей и HTTPS; проект не выдаёт себя за готовое production-решение.

## Локальный запуск без контейнеров приложения

Нужны уже доступные PostgreSQL и, для доставки событий, RabbitMQ. Устанавливать инфраструктуру автоматически проект не пытается.

```powershell
$env:ConnectionStrings__Database='Host=localhost;Database=securities;Username=demo'
$env:Database__Password='ВАШ_ПАРОЛЬ'
$env:Jwt__Key='ВАШ_СЛУЧАЙНЫЙ_КЛЮЧ_МИНИМУМ_32_БАЙТА'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://localhost:8080'
dotnet restore
dotnet run --project src/Securities.Api -- --init
dotnet run --project src/Securities.Api
# В отдельном терминале, с тем же ConnectionStrings__Database:
$env:RabbitMq__Host='localhost'
$env:RabbitMq__Username='demo'
$env:RabbitMq__Password='ВАШ_ПАРОЛЬ'
dotnet run --project src/Securities.Worker
# Ещё один терминал:
cd frontend/securities-web
npm.cmd ci
npm.cmd run dev
```

Vite проксирует `/api` на localhost:8080. Worker можно не запускать для работы учётного API: Outbox останется в PostgreSQL. Для создания схемы используется `EnsureCreated` плюс SQL-триггеры. Это **не система обновления существующей схемы**: при развитии модели потребуются EF migrations. Не применяйте `EnsureCreated` к существующей чужой базе.

## API и демонстрационный сценарий

Запросите токен `POST /api/dev/token` с телом `{"role":"Operator"}`. Ответ: `{"accessToken":"<JWT>"}`. В Swagger нажмите Authorize и введите токен. Auditor может читать, Admin и Operator могут изменять данные.

```http
POST /api/operations/transfer
Authorization: Bearer <JWT>
Content-Type: application/json

{
  "idempotencyKey": "demo-transfer-001",
  "sourceAccountId": "50000000-0000-0000-0000-000000000001",
  "targetAccountId": "50000000-0000-0000-0000-000000000002",
  "securityId": "40000000-0000-0000-0000-000000000001",
  "quantity": "80"
}
```

Ответ HTTP 200 содержит `id`, `type: "Transfer"`, `status: "Completed"`, `fundId`, `idempotencyKey`, даты и реквизиты операции. `GET /api/operations/{id}` возвращает объект `{operation, entries}`. После перевода остатки равны 20 и 80, проводки −80 и +80. Повторите тот же запрос — получите тот же идентификатор без новых проводок. Измените количество при том же ключе — HTTP 409.

Внешнее зачисление / списание использует те же поля без `targetAccountId` и обязательно содержит `movementParty` (источник/назначение) и `documentReference` (документ). Это явное внешнее движение с одной проводкой, не полная двойная запись.

```json
{"idempotencyKey":"credit-002","sourceAccountId":"50000000-0000-0000-0000-000000000001","securityId":"40000000-0000-0000-0000-000000000001","quantity":"10","movementParty":"Вымышленный внешний счёт","documentReference":"DEMO-DOC-002"}
```

`POST /api/operations/{id}/reverse`: `{"idempotencyKey":"reverse-001","reason":"Исправление ошибочного перевода"}`. Повторная корректировка с другим ключом запрещена. Корректировка может быть отклонена, если для обратного движения уже недостаточно остатка.

Ошибка имеет формат Problem Details, например HTTP 422:

```json
{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Недостаточный остаток.","status":422}
```

Чтение: `/api/funds`, `/api/funds/{id}`, `/positions` и `/operations` относительно фонда; `/api/securities`, `/api/custodians`, `/api/accounts`, `/api/management-companies`. История принимает `offset` и `limit` (1–200).

Сверка: `POST /api/reconciliations` с `{fundId, balances:[{custodyAccountId, securityId, quantity}]}`. Передаётся **полный** набор фонда; отсутствующий остаток считается нулём. Ответ содержит ожидаемые и фактические количества всех пар. Чтение: `/api/reconciliations/{id}`, `/api/funds/{id}/reconciliations`.

Оценка: `POST /api/valuations` с `{fundId, date:"2026-11-16", liabilities:"0", currency:"RUB"}`. Дата должна соответствовать имеющимся котировкам; seed создаёт их на текущую дату UTC. Ответ `{valuation, unitPrice}`. `PUT /api/prices` сохраняет `{securityId,priceDate,price,currency,source}`, `PUT /api/fund-units` — `{fundId,effectiveDate,unitsOutstanding}`. Чтение сохранённых оценок: `/api/funds/{id}/valuations`.

## Тестирование

```powershell
dotnet build
dotnet test
# При доступном Docker: настоящие PostgreSQL и RabbitMQ тесты.
$env:RUN_POSTGRES_TESTS='1'
$env:RUN_RABBITMQ_TESTS='1'
dotnet test tests/Securities.IntegrationTests
npm.cmd ci --prefix frontend/securities-web
npm.cmd run build --prefix frontend/securities-web
```

Без соответствующих `RUN_POSTGRES_TESTS=1` и `RUN_RABBITMQ_TESTS=1` контейнерные тесты **явно пропускаются**, а не засчитываются как успешные. CI устанавливает оба флага и выполняет тесты на Docker runner. InMemory / SQLite вместо PostgreSQL не используются. Проверяются конкурентные Credit/Debit, встречные переводы, Reverse, rollback, SQL-инварианты, decimal, повторный/прерванный seed и реальные транспортные сценарии RabbitMQ.

Текущий локальный результат: 24 теста прошли, 0 упали; 16 PostgreSQL и 5 RabbitMQ тестов пропущены из-за отсутствующего Docker. Release build и React production build прошли. Реальная доставка RabbitMQ и весь Compose локально не проверены. Смотрите [отчёт](docs/verification.md).

## Ограничения

Нет CSV, ETL, legacy migration, нормативного расчёта СЧА, FX, брокерских сделок, календарей торгов и автоматических корпоративных действий. Нет внешней интеграции для контроля остатков: они передаются вручную через API. Нет UI управления справочниками, котировками и паями — для последних есть API. Оценки заменяются по ключу фонд/дата, без версионирования расчётов. UI использует JavaScript number и предназначен для небольших демонстрационных количеств; серверная арифметика — только decimal/numeric(28,8). Список операций ограничен последними 100 записями в UI; API поддерживает пагинацию.

Не реализованы отдельный IdP, управление пользователями, аудит личности инициатора, эксплуатационные метрики, алерты, политика хранения Inbox/Outbox и резервное копирование. Блокировка всего счёта проста и надёжна, но снижает пропускную способность. Перед production нужны нагрузочные и отказоустойчивые проверки.

Работа coding agent и границы проверки описаны в [отчёте контроля](docs/agent-workflow.md). Никакой публикации в GitHub или на VPS не выполняется.

## Контракты и исправления критического ревью

Все количества и денежные величины в JSON — десятичные **строки**, включая ответы API: `"quantity":"9007199254740993.12345678"`, `"liabilities":"0"`. JSON numbers для decimal отклоняются с 400. Swagger отражает этот контракт. В UI значения хранятся строками; разность при сверке вычисляется через BigInt с масштабом 8. Для котировок, обязательств и результатов сохранения используется округление до 8 знаков ToEven и диапазон numeric(28,8); движения с лишними дробными знаками отклоняются. Активы суммируются точно в фиксированной арифметике с одним итоговым округлением, NAV вычисляется из уже нормализованных обязательств.

Worker ожидает готовности MassTransit topology, направляет persistent-событие в durable-очередь `securities-operation-completed` с mandatory и publisher confirms. `DeliveredAt` означает наличие сохранённого Inbox-факта потребителя, а не только ACK брокера. Повторы допустимы; exactly-once delivery не обещается. `CompletedAt` берётся из сохранённой операции. Health Worker требует работающих БД, шины и отсутствия неподтверждённых событий старше минуты.

Пароль PostgreSQL задаётся отдельной переменной `Database__Password` и помещается в connection string через NpgsqlConnectionStringBuilder, поэтому `;`, кавычки и `=` не меняют его синтаксис. Правила экранирования `.env` Docker Compose продолжают действовать (для буквального `$` используйте одинарные кавычки в `.env`).

Для полного прогона при доступном Docker:

```powershell
$env:RUN_POSTGRES_TESTS='1'
$env:RUN_RABBITMQ_TESTS='1'
dotnet test -c Release
```

Тесты создают собственные PostgreSQL/RabbitMQ контейнеры, не используют пользовательскую БД. В GitHub Actions оба флага обязательны, проверяется Docker и TRX: пропущенный интеграционный/транспортный тест делает workflow неуспешным. Локальные результаты и неподтверждённые сценарии: [verification](docs/verification.md). GitHub Actions и Compose в этой сессии не запускались.
