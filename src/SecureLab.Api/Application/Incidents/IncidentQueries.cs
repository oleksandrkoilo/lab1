using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = dbContext.Incidents.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // ЛР 1. Кількість інцидентів за severity.
    // Політика: лише наявні групи (рівень без інцидентів у відповідь не потрапляє,
    // порожня таблиця -> порожній масив).
    // Порядок: count за спаданням, при однаковому count - вищий рівень критичності першим.
    // Сортування виконується після матеріалізації, бо в БД severity зберігається як текст.
    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSummaryBySeverityAsync(
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Incidents
            .AsNoTracking()
            .GroupBy(incident => incident.Severity)
            .Select(group => new { Severity = group.Key, Total = group.Count() })
            .ToListAsync(cancellationToken);

        logger.LogInformation("Severity summary: {GroupCount} non-empty groups", rows.Count);

        return rows
            .OrderByDescending(row => row.Total)
            .ThenByDescending(row => row.Severity)
            .Select(row => new IncidentSeveritySummaryResponse(row.Severity.ToString(), row.Total))
            .ToList();
    }
}
