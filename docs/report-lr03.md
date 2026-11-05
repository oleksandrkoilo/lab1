# Звіт WEB ЛР03

## Відтворення (усі рівні)

- Обраний рівень: достатній («Задовільно»); автор: Койло Олександр Олександрович, КБ-43; baseline 2-A.
- Base tag `v0.2.0` → `5fe61a47243c1400e86d3caa5f8b8c315d20d68e`.
- Scaffold: release `lab-03-start-v1`, base_commit `5fe61a47243c1400e86d3caa5f8b8c315d20d68e` (`.scaffolds/lab-03.json`); встановлено окремим commit `a19b517` «Install lab-03 scaffold».
- Гілка `lab/3-authentication`. Commit власних змін: `90ff6d3` «Implement cookie authentication and protected incident creation». Тег `v0.3.0` — на commit зі звітом після повторних build/tests.
- ОС/SDK: Windows, .NET SDK 10.0.401 (`global.json`: 10.0.100, `latestFeature`).
- Команди: `dotnet build SecureLab.sln -c Release`; `docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait`; `dotnet run --project src/SecureLab.Api -- --reset-database`; `dotnet test tests/SecureLab.Api.Tests -c Release`. Пароль навчальних акаунтів — лише змінна `SeedUsers__Password` поточного процесу PowerShell.
- Фактичний результат: після scaffold — 9/9 тестів; після власних змін — 12/12 (evidence-lr03/tests-output.txt).
- Migration/reset: міграція `Lab03Identity` застосовується під час reset; `Reset-Twice.ps1` — двічі «Відновлення…/очищено…», обидва рази `Passed: 9, Failed: 0`, `$LASTEXITCODE = 0` (evidence-lr03/reset-twice.txt).

## Власна реалізація і межі

**DTO boundary.** Register приймає `RegistrationInput` і повертає лише `RegisteredUserResponse(Id, UserName, DisplayName)`. Правила винесено в клас `RegistrationRules`: усі поля обов'язкові; userName ≤ 256 і лише символи `a-z A-Z 0-9 - . _ @ +`; email ≤ 256 і формат за `EmailAddressAttribute`; displayName ≤ 120 (обмеження стовпця БД). Порушення полів → 400 `ValidationProblem`. Будь-яка відмова `UserManager.CreateAsync` (дублікат, слабкий пароль) → однаковий 400 Problem Details «Реєстрацію не виконано». Нормалізується лише `Trim()` текстових полів; пароль передається Identity без змін і ніде не логується.

**Звідки береться id.** `GET /api/me` має `.RequireAuthorization()` і отримує `ClaimsPrincipal`; `UserManager.GetUserAsync(principal)` знаходить користувача за claim `NameIdentifier`, відповідь — `ProfileResponse(UserId, UserName, DisplayName, Roles)`. `X-Demo-UserId` більше не використовується. `POST /api/incidents` має `.RequireAuthorization()`, `OwnerUserId` визначається методом `TryGetUserId(ClaimsPrincipal)`; `ownerUserId` у JSON ігнорується, бо його немає в `CreateIncidentRequest`.

**Cookie options (`AddLab03Identity`).** `HttpOnly = true` (cookie недоступна JavaScript), `SameSite = Lax` (не надсилається у міжсайтових POST), `SecurePolicy = SameAsRequest` у Development і `Always` в інших середовищах. Локальний стенд працює по HTTP, тому відсутність `Secure` у локальному трасуванні не свідчить про production-поведінку. Строк дії стандартний. `OnRedirectToLogin` дає 401 замість 302.

**Login.** `SignInManager.PasswordSignInAsync`; хибний пароль і невідомий користувач дають однаковий 401 (evidence-lr03/login-uniform-401.txt); поведінку не змінено.

**Маршрут довіри:** login → `PasswordSignInAsync` (перевірка пароля з `PasswordHash`) → `Set-Cookie: .AspNetCore.Identity.Application` → клієнт автоматично надсилає cookie → cookie authentication handler перевіряє її → `ClaimsPrincipal` у `HttpContext.User` → `/api/me` і `OwnerUserId`.

**Перевірка пароля.** Хешування і перевірку виконує стандартний `PasswordHasher` Identity (сіль, багато ітерацій). У `identity_users` є лише `PasswordHash text` (T-03); hash не входить до жодного response DTO.

**Client UserId.** Заголовок, body чи query формує клієнт, тому вони не доводять особу: до змін `X-Demo-UserId` Bob без cookie повертав профіль Bob. Доказом є лише cookie, видана сервером після перевірки пароля.

**CSRF навчальний борг.** Cookie надсилається автоматично; `SameSite=Lax` закриває міжсайтові POST, але не запити з того самого сайту. Антифорджері-токенів немає — це запланований зміст ЛР 6.

**Канали перебору акаунтів.** Login — однаковий 401. Register — відмова однакова для дубліката й слабкого пароля, але за сильного пароля й коректного email 400 вказує на зайняте ім'я/email; це прийнятий компроміс. Обмеження спроб входу не входить до обраного рівня — залишковий ризик підбору.

Logout, A-03/A-04/A-06/A-07, rate limit, події, перевірка flags, браузерний сценарій, duplicate 400/409, поглиблення — не входять до обраного рівня.

## Докази

| ID | Рівень | Передумова/дія | Очікувано | Фактично | Безпечний доказ |
|---|---|---|---|---|---|
| T-01 | достатній | reset; register `student…` з одноразовим паролем | safe 201 | 201, `Location: /api/me`, лише `id`, `userName`, `displayName` | evidence-lr03/t01-register.txt |
| T-02 | достатній | повтор register; некоректні поля | safe rejection | 400 «Реєстрацію не виконано»; 400 ValidationProblem (userName, email, displayName) | evidence-lr03/t02-register-duplicate-invalid.txt |
| T-03 | достатній | `\d identity_users` + Identity verifier | password не відкрито | `PasswordHash text`, стовпця пароля немає | evidence-lr03/t03-identity-users-schema.txt |
| A-01 | достатній | анонімний `POST /api/incidents` | 401, no redirect | `A01_CreateIncidentWithoutCookie_IsRejectedWith401` Passed; вручну 401 | tests-output.txt, create-before-after-a05.txt |
| A-02 | достатній | login → `/api/me` | principal user | `A02_ProfileAfterLogin_DescribesAuthenticatedUser` Passed (Bob, роль Reporter); вручну 200 alice | tests-output.txt, me-before-after.txt |
| A-05 | достатній | Bob з `ownerUserId` Alice | owner = Bob | `A05_ForgedOwnerInBody_IsIgnored_OwnerComesFromPrincipal` Passed; вручну 201 і «Боб Мельник» | tests-output.txt, create-before-after-a05.txt |
| S-01 | достатній | відповіді, логи, diff | без витоків | у відповідях немає полів password/stamp/token/cookie (A-02); логування в `Scaffolding` немає; значення cookie приховано | цей звіт |
| A-03/04/06/07 | добрий | — | — | не входить до обраного рівня | — |

До змін: `/api/me` без header — 501; з `X-Demo-UserId` Bob без cookie — 200 і профіль Bob; анонімний `POST /api/incidents` — 201. Після змін — 401 у всіх трьох випадках.

## Декларація використання ШІ

ШІ використано: Claude (Anthropic), застосунок Claude у режимі Cowork. Пароль навчальних акаунтів ШІ не передавався — його введено вручну у вікні PowerShell.

| Дата | Сервіс/модель | Завдання | Використана частина відповіді | Авторська адаптація | Перевірка і фактичний результат |
|---|---|---|---|---|---|
| 2026-11-05 | Claude (Anthropic) | розбір методички ЛР03, план рівня «Задовільно» | перелік обов'язкових T/A/S | обрано рівень «Задовільно» | звірено з таблицею рівнів |
| 2026-11-05 | Claude | `RegistrationRules`, `/api/me` через `GetUserAsync`, захист create з `ClaimsPrincipal` | код endpoint-ів і DTO | розібрано маршрут cookie → principal → owner | build; ручні запити до/після |
| 2026-11-05 | Claude | `Lab03SecurityTests` (A-01, A-02, A-05), вхід у `Lab02ValidationTests` | тести | — | `dotnet test`: 12/12 |
| 2026-11-05 | Claude | PowerShell-скрипти етапів, витяг доказів, чернетка звіту | скрипти й текст | пароль вводився вручну; скрипти запускались на власному стенді | вивід і скріншоти переглянуто; паролів і значень cookie немає |
