using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MiniBank.Api;

internal static class MvpWrite
{
    public static async Task<IResult> Run(BankDb db, Func<Task<IResult>> change)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var result = await change();
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception e) when (Retryable(e))
            {
                if (attempt == 3) return Error(409, "Sočasna sprememba. Ponovite zahtevo.");
                db.ChangeTracker.Clear();
            }
        }
    }

    private static bool Retryable(Exception error)
    {
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause is PostgresException postgres && postgres.SqlState is
                PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
                return true;
        return false;
    }

    public static Guid Id(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Task<bool> HasAccess(BankDb db, ClaimsPrincipal user, string role)
    {
        var id = Id(user);
        var stamp = Guid.Parse(user.FindFirstValue("stamp")!);
        return db.Users.AnyAsync(x => x.Id == id && x.Active && x.Role == role && x.SecurityStamp == stamp);
    }

    public static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);

    public const decimal MaxAmount = 9_999_999_999_999.99m;
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    public static bool Amount(string? input, out decimal value) =>
        decimal.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
        && value >= 0 && value <= MaxAmount
        && (input!.IndexOf('.') < 0 || input.Length - input.IndexOf('.') <= 3);
}
