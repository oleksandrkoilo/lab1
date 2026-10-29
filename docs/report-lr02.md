# Звіт WEB ЛР 2

## 1. Ідентифікація

- ПІБ і група: Койло Олександр Олександрович, КБ-43.
- Спільний baseline 2-A; індивідуального номера немає.
- Заявлений рівень: добрий («Добре»).
- Гілка: `lab/2-input-sqli` (від власного тегу `v0.1.0`).
- Квитанція `.scaffolds/lab-02.json`: release `lab-02-start-v1`, base_commit `dc50517606560ba58f9f5b5b6c2f83a433452b6c` (= `v0.1.0`), entry_parent `20405eca08104528329f8d747828420dffa9180e` («Update .NET 10 SDK selection policy»).
- Vulnerable commit: `def941ee5c470751fd9269cdff770d90d6e018e8` «Validate create contract; preserve search evidence».
- Fixed commit коду: `f41ac79eefa29f4ea02fb7b0119d201d74ca3e88` «Validate incident input and secure search query».
- Тег `v0.2.0` — на наступному commit зі звітом (код не змінює).
- DEL-01: https://github.com/oleksandrkoilo/lab1 (гілка `lab/2-input-sqli`, тег `v0.2.0`).

## 2. Контракт POST /api/incidents і CP-01

Реалізацію виконано в `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`. Перевірки окремих полів винесено в метод `ValidateCreateRequest`, який повертає словник помилок і перетворене значення severity; межі задано константами (`MaxTitleLength = 160`, `MaxDescriptionLength = 4000`, `MinDescriptionForHighSeverity = 40`, `FutureTolerance = 5 хв`).

| Поле | Правило | Відповідь |
|---|---|---|
| title | обов'язкове; ≤ 160 символів до `Trim()` | 400, ключ `title` |
| description | обов'язкове; ≤ 4000 символів до `Trim()` | 400, ключ `description` |
| severity | `Enum.TryParse(ignoreCase: true)` і `Enum.IsDefined` | 400, ключ `severity` |
| occurredAtUtc | обов'язкове; ≤ `now + 5 хв` (now фіксується один раз) | 400, ключ `occurredAtUtc` |
| T-09 | `High`/`Critical` → опис після `Trim()` ≥ 40 | 400, ключ `description` |
| T-03 | та сама назва після `Trim()` (з урахуванням регістру) серед New/Triaged/InProgress/Resolved | 409 Problem Details |

Помилки полів повертаються через `Results.ValidationProblem` (400, `application/problem+json`, `errors`). Після перевірок `Title`/`Description` нормалізуються `Trim()` один раз. Сервер задає `Id`, `OwnerUserId = DbSeeder.AliceId`, `Status = New`, `CreatedAtUtc/UpdatedAtUtc`, а `OccurredAtUtc` зберігається після `ToUniversalTime()` (`15:45+02:00` → `13:45Z`). Відповідь 201 — `IncidentCreatedResponse(Id, Title, Severity, Status, OccurredAtUtc, CreatedAtUtc)` із заголовком `Location`.

`Enum.TryParse` перетворює і назви, і числові рядки: `"7"` стає `(IncidentSeverity)7`, якого немає серед значень enum; `Enum.IsDefined` відкидає таке значення (evidence/t02-severity-7.txt). Маршрут: JSON → `CreateIncidentRequest` → `ValidateCreateRequest` → `ValidationProblem` до `db.Incidents.Add`. Обмеження `<select>` у браузері не замінює серверної перевірки, бо запит можна сформувати поза формою.

Overposting (CP-01): у тілі запиту додатково передано `id`, `status: "Resolved"`, `ownerUserId` (Bob) і `createdAtUtc`. Створений запис має новий id, статус `New` і власника «Аліса Коваль» — у request DTO цих властивостей немає (evidence/cp01-overposting.txt).

## 3. Security-сценарій, CP-02 і CP-03

**Стан до.** Local Development, PostgreSQL з `infra/compose.yaml`, штучний seed (5 записів після reset), vulnerable commit `def941e`.

**Першопричина.** `Lab02Endpoints.cs`, метод `MapLab02Endpoints`, обробник `GET /api/incidents/search`, рядки 19–21 vulnerable commit: `q` конкатенацією вставлявся всередину SQL-літерала, а рядок виконувався через `FromSqlRaw(sql)`; `sortBy` гілкою `_ => sortBy` потрапляв у `ORDER BY`.

| Запит | Status | Результат |
|---|---|---|
| A `q=USB` | 200 | 1 запис «Перевірка USB навчальної мережі» |
| B `q=zz-no-match` | 200 | `[]` |
| C `q=zz-no-match' OR TRUE -- ` | 200 | **5 записів** (ids …0001–…0005) |
| D `q=O'Brien` | 500 | у журналі `42601: syntax error at or near "Brien"` |
| D2 `q=комп'ютерного` | 500 | у журналі `42601: syntax error at or near "ютерного"` |

У журналі `Parameters=[]`: значення не передавалося окремо від тексту команди. Апостроф у C завершив рядковий літерал, `OR TRUE` став частиною `WHERE`, `--` закоментував решту. Фільтрація апострофа чи `OR` не усуває механізм і ламає легітимні запити.

**Виправлення.** `FromSqlRaw` прибрано. Пошук — LINQ `Where(EF.Functions.ILike(item.Title, likePattern, @"\") || EF.Functions.ILike(item.Description, likePattern, @"\"))`; `likePattern` будується з `EscapeLikePattern`, що екранує `\`, `%`, `_`. У журналі після виправлення — `Parameters=[@likePattern='?', @likePattern0='?', @p='?']`. Сортування — метод `ApplySearchOrder` з allowlist (`createdAtUtc` за замовчуванням спаданням, ранги severity Critical > High > Medium > Low, status New > Triaged > InProgress > Resolved > Closed; інше → `null` → 400 з ключем `sortBy`), далі `ThenBy(Id)` і `Take(50)`. Відповідь — `IncidentSearchItem`. Diff `def941e..f41ac79`: 2 файли, +120/−12.

**Після виправлення:** C → 200 `[]`; A → 1 запис; `O'Brien` → 1 запис; `комп'ютерного` → 1 запис; `q=%` і `q=_` → `[]`.

## 4. Фактичні перевірки DEL-02

| ID | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|
| T-01 | Коректне створення | reset | POST, дата `+02:00` | 201, Location, DTO | 201, status New, occurredAtUtc `13:45Z` | evidence/t01-create.txt |
| T-02 | Некоректний DTO | reset | `severity:"7"`; без title і дати, порожній опис, `"Severe"`; дата 2099; опис 4001 | 400 з ключами полів | 400 severity; 400 title/description/severity/occurredAtUtc; 400 occurredAtUtc; 400 description | evidence/t02-*.txt |
| T-02 авто | `T02_SeverityOutsideEnum_IsRejectedWithProblemDetails` | PostgreSQL | `dotnet test` | 400, problem+json, errors.severity | Passed | evidence/tests-output.txt |
| T-03 | Конфлікт | seed «Підозрілий лист із вкладенням» (Triaged) | та сама назва; з пробілами; великими літерами | 409; 409; 201 | 409; 409; 201 | evidence/t03-conflict.txt |
| T-03 авто | `T03_SecondActiveIncidentWithSameTitle_Returns409` | PostgreSQL | два POST, cleanup у `finally` | 201, 409 | Passed | evidence/tests-output.txt |
| T-06 | Неіснуючий id | API | GET `/api/incidents/99999999-…` | 404 problem+json | 404 «Інцидент не знайдено» | evidence/t06-404.txt |
| T-09 | Межа cross-field | reset | Critical + 39 / 40 символів після Trim | 400 / 201 | 400 description / 201 | evidence/t09-39-40.txt |
| CP-01 | Overposting | API | POST із id/status/ownerUserId/createdAtUtc + GET | New, Alice | New, «Аліса Коваль», новий id | evidence/cp01-overposting.txt |
| S-01 | Ввід до fix | `def941e`, reset | контрольний ввід C | без дефекту `[]` | 200, 5 записів | evidence/s01-vulnerable-search.txt |
| S-02 | Ввід після fix | `f41ac79`, reset | той самий ввід | 200 `[]` | 200 `[]` | evidence/s02-t04-fixed-search.txt |
| T-04 | Позитивна регресія | `f41ac79`, reset | USB, O'Brien, комп'ютерного | 200, по 1 запису | 200, по 1 запису | evidence/s02-t04-fixed-search.txt |
| T-05 | Невідоме сортування | `f41ac79` | `sortBy=owner` | 400, ключ sortBy | 400 `errors.sortBy` | evidence/t05-sortby.txt |
| A-01 | Пошук raw SQL | `f41ac79` | `data-access-scan.py`, `git grep` | класифікація | 1 збіг — нижче | evidence/a01-scan.txt |
| Тести | Build + tests | `f41ac79`, reset | `dotnet build`, `dotnet test -c Release` | зелені | Passed 8 / Failed 0 | evidence/tests-output.txt |

**A-01.** Єдиний збіг після виправлення — `src/SecureLab.Api/Data/DatabaseBootstrap.cs:29`, `ExecuteSqlRawAsync("TRUNCATE TABLE …")`: статичний службовий SQL без даних від клієнта, виконується лише з `--reset-database` у Development — безпечно. Збіг `FromSqlRaw` у пошуку усунено.

## 5. Обмеження і висновок

Було реалізовано серверну валідацію контракту створення інциденту з єдиним форматом Problem Details, збережено й досліджено vulnerable state пошуку, підтверджено SQL-ін'єкцію єдиним дозволеним read-only вводом і усунуто першопричину через LINQ/`ILike` з параметром та allowlist сортування.

Обмеження: попередня перевірка `AnyAsync` не гарантує унікальності за паралельних POST — потрібен частковий унікальний індекс або транзакція; migration у ЛР 2 не додавалась. Помилки читання JSON окремо не відтворювались. Числовий рядок визначеного значення (`"3"`) `TryParse` + `IsDefined` приймають як `Critical`.

## 6. Декларація використання ШІ

| Сервіс | Задачі | Авторська адаптація/інтерпретація | Фактична перевірка |
|---|---|---|---|
| Claude (Anthropic), застосунок Claude у режимі Cowork | Аналіз методички й план етапів; генерація коду `ValidateCreateRequest`, POST-обробника, `IncidentCreatedResponse`, виправленого пошуку (`ApplySearchOrder`, `EscapeLikePattern`, `ILike`) і тестів `Lab02ValidationTests`; сценаріїв `tests/http/lab-02-checks.http`; PowerShell-скриптів для етапів і скріншотів; витяг доказів у `docs/evidence/`; чернетка звіту й docx | Запуск скриптів на власному стенді, перегляд відповідей і скріншотів; розбір пояснень щодо `TryParse` + `IsDefined`, механізму SQLi і параметризації; вибір рівня «Добре» | Build без помилок; `dotnet test` Passed 8/8; ручні перевірки розділу 4 з фактичними статусами; журнал API до/після (`Parameters=[]` → `@likePattern`) |

## 7. Передавання

- Гілку `lab/2-input-sqli` і тег `v0.2.0` надіслано до https://github.com/oleksandrkoilo/lab1.
- Звіт і докази не містять connection string, паролів, токенів, cookies чи дампів БД; повний SQL не наведено.
- Звіт (docx) і докази завантажуються в Moodle.
