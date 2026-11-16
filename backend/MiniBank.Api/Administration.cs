using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static MiniBank.Api.MvpWrite;

namespace MiniBank.Api;

internal static class Administration
{
    public static void MapAdministration(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/users").RequireAuthorization(p => p.RequireRole("ADMIN"));
        admin.MapGet("", List);
        admin.MapPost("", Create);
        admin.MapPost("/{id:guid}/access", Update);
    }

    private static async Task<IResult> List(BankDb db) => Results.Ok(await db.Users.AsNoTracking()
        .OrderBy(u => u.Email).Select(u => new { u.Id, u.Name, u.Email, u.Role, u.Active }).ToListAsync());

    private static Task<IResult> Create(NewUser input, ClaimsPrincipal user, BankDb db, IPasswordHasher<AppUser> hasher)
    {
        var name = input.Name?.Trim();
        var email = input.Email?.Trim().ToLowerInvariant();
        var password = input.Password;
        if (name is null or "" || name.Length > 100 || email is null or "" || email.Length > 254
            || !new EmailAddressAttribute().IsValid(email) || !ValidRole(input.Role)
            || string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 256)
            return Task.FromResult(Error(400, "Vnesite ime, veljavno e-pošto, vlogo in geslo z 12–256 znaki."));
        var account = new AppUser { Name = name, Email = email, Role = input.Role! };
        account.PasswordHash = hasher.HashPassword(account, password);
        return Run(db, async () =>
        {
            if (!await HasAccess(db, user, "ADMIN")) return Results.Unauthorized();
            if (await db.Users.AnyAsync(u => u.Email == email)) return Error(409, "E-pošta je že v uporabi.");
            db.Users.Add(account);
            return Results.Json(new { account.Id, account.Name, account.Email, account.Role, account.Active }, statusCode: 201);
        });
    }

    private static Task<IResult> Update(Guid id, UserAccess input, ClaimsPrincipal user, BankDb db) =>
        Run(db, async () =>
        {
            if (!await HasAccess(db, user, "ADMIN")) return Results.Unauthorized();
            if (!ValidRole(input.Role) || input.Active is null) return Error(400, "Izberite veljavno vlogo in stanje dostopa.");
            var account = await db.Users.FindAsync(id);
            if (account is null) return Results.NotFound();
            if (account.Active == input.Active && account.Role == input.Role) return Results.NoContent();
            if (account.Role == "CLIENT" && input.Role != "CLIENT"
                && (await db.Accounts.AnyAsync(a => a.OwnerId == id)
                    || await db.Memberships.AnyAsync(m => m.UserId == id && m.Status == Groups.Member)))
                return Error(409, "Komitentu z računom ali članstvom ni mogoče spremeniti vloge.");
            if (account.Active && account.Role == "ADMIN" && (input.Active == false || input.Role != "ADMIN")
                && !await db.Users.AnyAsync(u => u.Id != id && u.Role == "ADMIN" && u.Active))
                return Error(409, "Potreben je vsaj en aktiven administrator.");
            account.Role = input.Role!;
            account.Active = input.Active.Value;
            account.SecurityStamp = Guid.NewGuid();
            return Results.NoContent();
        });

    private static bool ValidRole(string? role) => role is "CLIENT" or "EMPLOYEE" or "ADMIN";
}

internal record NewUser(string? Name, string? Email, string? Password, string? Role);
internal record UserAccess(string? Role, bool? Active);
