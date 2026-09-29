# Карта архітектури (після ЛР 1)

## Компоненти системи

| Компонент | Де знаходиться | Що робить |
|---|---|---|
| Браузерний клієнт | `src/SecureLab.Api/Client/` | HTML + Vanilla JS: надсилає `fetch`-запити й виводить результат через DOM API (`textContent`) |
| API (Presentation) | `Presentation/Endpoints/IncidentEndpoints.cs`, `Presentation/Contracts/IncidentResponses.cs` | Маршрути `/api/incidents/...`, перевірка вхідних параметрів, HTTP-відповіді та DTO |
| Application | `Application/Incidents/IncidentQueries.cs` | Запити читання: список, деталі, підсумок за severity |
| Data (EF Core + Npgsql) | `Data/SecureLabDbContext.cs`, `Data/Migrations`, `Data/DbSeeder.cs` | Зв'язок entity з таблицями, migration, seed і reset |
| PostgreSQL | `infra/compose.yaml` | Локальна БД у Docker, порт `127.0.0.1:54329` |

## Новий маршрут: підсумок за severity

```text
кнопка #severity-button (Client/index.html)
  → showSeveritySummary (Client/app.js)
  → GET /api/incidents/severity-summary
  → IncidentEndpoints.GetSummaryBySeverityAsync
  → IncidentQueries.GetSummaryBySeverityAsync
  → SecureLabDbContext.Incidents / таблиця incidents (GROUP BY severity)
  → List<IncidentSeveritySummaryResponse> → JSON
  → рядки таблиці #severity-rows через textContent
```

Рішення щодо контракту:

- відповідь — масив `{ severity, count }`, інших полів немає;
- політика нульових груп — **лише наявні групи**: рівень без інцидентів не повертається, порожня таблиця дає `[]`;
- порядок — **count за спаданням**, при рівному count першим іде вищий рівень критичності (сортування після матеріалізації, бо `severity` у БД — текст);
- на seed: `High 1, Medium 1, Low 1` (Critical відсутній).

## Досліджений маршрут деталей

```text
клік по картці → loadIncidentDetails (app.js) → GET /api/incidents/{id}
  → IncidentEndpoints.GetDetailsAsync (/{id:guid}, null → 404)
  → IncidentQueries.GetDetailsAsync (AsNoTracking, Where, Select, SingleOrDefaultAsync)
  → SecureLabDbContext.Incidents → incidents
  → IncidentDetailsResponse → JSON → renderIncidentDetails (textContent, createTextNode)
```

## Межі довіри

| Межа | Дані | Чого не можна припускати | Контроль |
|---|---|---|---|
| Браузер → API | URL, path `id`, query `status`, заголовки | що запит прийшов саме з нашої форми | `:guid`, перевірка enum (400), обробка `null` (404) |
| API → PostgreSQL | параметри умов запиту | що значення безпечні для SQL | параметризовані запити EF Core, `AsNoTracking()` |
| API → браузер | JSON-відповідь | що клієнту можна віддати всі поля entity | окремі DTO; summary містить лише `severity` і `count` |
| Відповідь → DOM | текстові поля | що текст із БД не містить HTML | `textContent`, `createTextNode`; тест забороняє `innerHTML` |

## Конфігурація

- `global.json` — .NET SDK смуги 10.0.3xx;
- `appsettings.json`, `appsettings.Development.json` — налаштування та локальний connection string навчального стенда;
- `infra/compose.yaml` + `infra/.env.example` — контейнер PostgreSQL і порт;
- `ConnectionStrings__SecureLab` — змінна середовища для перевизначення рядка підключення поза Git.

## Повернення до початкового стану

`dotnet run --project src/SecureLab.Api -- --reset-database` — очищує навчальні таблиці та заново заповнює seed (лише Development).
