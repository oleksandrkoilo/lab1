using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
        {
            // ЛР 03: перевірка контракту реєстрації до звернення до Identity
            var problems = RegistrationRules.Check(input);
            if (problems.Count > 0)
                return Results.ValidationProblem(problems);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = input.UserName!.Trim(),
                Email = input.Email!.Trim(),
                DisplayName = input.DisplayName!.Trim()
            };
            // пароль передається Identity як є: хешування виконує стандартний PasswordHasher
            var result = await users.CreateAsync(user, input.Password!);
            if (!result.Succeeded)
            {
                // одна узагальнена відмова: не розкриваємо, чи зайняте ім'я, чи слабкий пароль
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Реєстрацію не виконано",
                    detail: "Перевірте дані та спробуйте ще раз.");
            }
            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            return Results.Created("/api/me", new RegisteredUserResponse(user.Id, user.UserName!, user.DisplayName));
        });
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
                return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        app.MapPost("/api/auth/logout", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        // ЛР 03: профіль поточного користувача — лише з автентифікованого principal
        app.MapGet("/api/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var current = await users.GetUserAsync(principal); // читає NameIdentifier з principal
            if (current is null)
                return Results.Unauthorized();
            var roles = await users.GetRolesAsync(current);
            return Results.Ok(new ProfileResponse(current.Id, current.UserName ?? string.Empty,
                current.DisplayName, roles.Order().ToArray()));
        }).RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegisteredUserResponse(Guid Id, string UserName, string DisplayName);
public sealed record ProfileResponse(Guid UserId, string UserName, string DisplayName, string[] Roles);

// ЛР 03: правила зовнішнього контракту реєстрації
public static class RegistrationRules
{
    public const int MaxUserNameLength = 256;
    public const int MaxEmailLength = 256;
    public const int MaxDisplayNameLength = 120; // обмеження стовпця DisplayName у БД
    private const string AllowedUserNameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    private static readonly EmailAddressAttribute EmailFormat = new();

    public static Dictionary<string, string[]> Check(RegistrationInput input)
    {
        var problems = new Dictionary<string, string[]>();

        var userName = input.UserName?.Trim();
        if (string.IsNullOrEmpty(userName))
            problems["userName"] = ["Поле userName є обов'язковим."];
        else if (userName.Length > MaxUserNameLength)
            problems["userName"] = [$"Поле userName має містити не більше {MaxUserNameLength} символів."];
        else if (userName.Any(symbol => !AllowedUserNameChars.Contains(symbol)))
            problems["userName"] = ["Поле userName допускає лише латинські літери, цифри та - . _ @ +"];

        var email = input.Email?.Trim();
        if (string.IsNullOrEmpty(email))
            problems["email"] = ["Поле email є обов'язковим."];
        else if (email.Length > MaxEmailLength)
            problems["email"] = [$"Поле email має містити не більше {MaxEmailLength} символів."];
        else if (!EmailFormat.IsValid(email))
            problems["email"] = ["Поле email має некоректний формат."];

        var displayName = input.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName))
            problems["displayName"] = ["Поле displayName є обов'язковим."];
        else if (displayName.Length > MaxDisplayNameLength)
            problems["displayName"] = [$"Поле displayName має містити не більше {MaxDisplayNameLength} символів."];

        if (string.IsNullOrEmpty(input.Password))
            problems["password"] = ["Поле password є обов'язковим."];

        return problems;
    }
}
