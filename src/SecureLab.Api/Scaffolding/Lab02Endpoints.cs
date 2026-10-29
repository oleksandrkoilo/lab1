using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            // ЛР 02: власні перевірки контракту 2-A до створення entity та звернення до БД
            var now = DateTimeOffset.UtcNow;
            var errors = ValidateCreateRequest(request, now, out var severity);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var title = request.Title!.Trim();
            var description = request.Description!.Trim();

            // cross-field правило T-09 перевіряється вже на нормалізованому описі
            if (severity is IncidentSeverity.High or IncidentSeverity.Critical
                && description.Length < MinDescriptionForHighSeverity)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["description"] = [$"Інцидент рівня {severity} потребує опису щонайменше з {MinDescriptionForHighSeverity} символів."]
                });
            }

            // предметний конфлікт T-03: така сама назва серед активних (не Closed) інцидентів
            var titleTaken = await db.Incidents.AnyAsync(item => item.Title == title
                && (item.Status == IncidentStatus.New || item.Status == IncidentStatus.Triaged
                    || item.Status == IncidentStatus.InProgress || item.Status == IncidentStatus.Resolved), ct);
            if (titleTaken)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Конфлікт назви інциденту",
                    detail: "Незакритий інцидент із такою назвою вже існує. Оберіть іншу назву.");
            }

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/incidents/{incident.Id}", new IncidentCreatedResponse(
                incident.Id, incident.Title, incident.Severity.ToString(), incident.Status.ToString(),
                incident.OccurredAtUtc, incident.CreatedAtUtc));
        });
    }

    private const int MaxTitleLength = 160;
    private const int MaxDescriptionLength = 4000;
    private const int MinDescriptionForHighSeverity = 40;
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    // Перевірки окремих полів; усі помилки збираються в один словник
    private static Dictionary<string, string[]> ValidateCreateRequest(
        CreateIncidentRequest request, DateTimeOffset now, out IncidentSeverity severity)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
            errors["title"] = ["Поле title є обов'язковим."];
        else if (request.Title.Length > MaxTitleLength)
            errors["title"] = [$"Поле title має містити не більше {MaxTitleLength} символів."];

        if (string.IsNullOrWhiteSpace(request.Description))
            errors["description"] = ["Поле description є обов'язковим."];
        else if (request.Description.Length > MaxDescriptionLength)
            errors["description"] = [$"Поле description має містити не більше {MaxDescriptionLength} символів."];

        // TryParse прийме і числовий рядок ("7"), тому IsDefined обов'язковий
        if (!Enum.TryParse(request.Severity, ignoreCase: true, out severity) || !Enum.IsDefined(severity))
            errors["severity"] = ["Поле severity приймає лише Low, Medium, High або Critical."];

        if (request.OccurredAtUtc is not { } occurredAt)
            errors["occurredAtUtc"] = ["Поле occurredAtUtc є обов'язковим."];
        else if (occurredAt > now.Add(FutureTolerance))
            errors["occurredAtUtc"] = ["Поле occurredAtUtc не може випереджати поточний час більш ніж на 5 хвилин."];

        return errors;
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record IncidentCreatedResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);
