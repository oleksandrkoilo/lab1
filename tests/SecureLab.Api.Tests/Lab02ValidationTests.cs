using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 02: регресійні тести контракту POST /api/incidents
public sealed class Lab02ValidationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    // ЛР 03: створення інциденту вимагає входу, тому перевірки контракту виконуються від імені Alice
    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task T02_SeverityOutsideEnum_IsRejectedWithProblemDetails()
    {
        var payload = new
        {
            title = "T-02 автоматичний тест",
            description = "Опис для автоматичної перевірки severity.",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        using var client = await factory.LoginAsync("alice", SeedPassword);
        using var response = await client.PostAsJsonAsync("/api/incidents", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
        Assert.False(problem.RootElement.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task T03_SecondActiveIncidentWithSameTitle_Returns409()
    {
        var title = $"T03-{Guid.NewGuid():N}";
        var payload = new
        {
            title,
            description = "Перевірка конфлікту однакових назв.",
            severity = "Medium",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        using var client = await factory.LoginAsync("alice", SeedPassword);
        try
        {
            using var created = await client.PostAsJsonAsync("/api/incidents", payload);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var duplicate = await client.PostAsJsonAsync("/api/incidents", payload);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            // видаляємо тестовий запис, щоб база лишалась у відомому стані
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }
}
