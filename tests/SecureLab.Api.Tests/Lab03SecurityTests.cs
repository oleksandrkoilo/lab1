using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 03: обов'язкові security-сценарії рівня «Задовільно» (A-01, A-02, A-05)
public sealed class Lab03SecurityTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task A01_CreateIncidentWithoutCookie_IsRejectedWith401()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        var payload = new
        {
            title = $"A01-{Guid.NewGuid():N}",
            description = "Спроба створення без автентифікації.",
            severity = "Medium",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15)
        };

        using var response = await anonymous.PostAsJsonAsync("/api/incidents", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);                 // немає redirect на сторінку входу
        Assert.Empty(response.Headers.WwwAuthenticate);          // немає несподіваного Bearer-виклику
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A02_ProfileAfterLogin_DescribesAuthenticatedUser()
    {
        using var bob = await factory.LoginAsync("bob", SeedPassword);

        using var response = await bob.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var profile = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = profile.RootElement;
        Assert.Equal(DbSeeder.BobId, root.GetProperty("userId").GetGuid());
        Assert.Equal("bob", root.GetProperty("userName").GetString());
        Assert.Contains(root.GetProperty("roles").EnumerateArray(), role => role.GetString() == DbSeeder.ReporterRole);
        var names = root.EnumerateObject().Select(property => property.Name.ToLowerInvariant()).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("password") || name.Contains("stamp")
                                          || name.Contains("token") || name.Contains("cookie"));
    }

    [Fact]
    public async Task A05_ForgedOwnerInBody_IsIgnored_OwnerComesFromPrincipal()
    {
        var title = $"A05-{Guid.NewGuid():N}";
        using var bob = await factory.LoginAsync("bob", SeedPassword);
        var forged = new
        {
            title,
            description = "Bob намагається призначити власником Alice.",
            severity = "Medium",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15),
            ownerUserId = DbSeeder.AliceId
        };

        try
        {
            using var response = await bob.PostAsJsonAsync("/api/incidents", forged);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var ownerUserId = await db.Incidents.AsNoTracking()
                .Where(item => item.Title == title)
                .Select(item => item.OwnerUserId)
                .SingleAsync();
            Assert.Equal(DbSeeder.BobId, ownerUserId);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }
}
