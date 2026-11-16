using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static MiniBank.Api.MvpWrite;

namespace MiniBank.Api;

internal static class Transfers
{
    public const string Pending = "PENDING_APPROVAL";
    public const string Executed = "EXECUTED";
    public const string Rejected = "REJECTED";
    public const string Failed = "FAILED";

    public static void MapTransfers(this WebApplication app)
    {
        var client = app.MapGroup("/api/transfers").RequireAuthorization(p => p.RequireRole("CLIENT"));
        client.MapPost("", Submit);
        client.MapGet("/sent/{accountId:guid}", Sent);
        var employee = app.MapGroup("/api/review/transfers").RequireAuthorization(p => p.RequireRole("EMPLOYEE"));
        employee.MapGet("", Queue);
        employee.MapPost("/{id:guid}/decision", Decide);
    }

    private static async Task<IResult> Sent(Guid accountId, int? page, ClaimsPrincipal user, BankDb db)
    {
        var owner = Id(user);
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.OwnerId == owner)) return Results.NotFound();
        var offset = Offset(page);
        if (offset < 0) return Error(400, "Neveljavna stran.");
        var rows = await db.Transfers.AsNoTracking().Where(t => t.SourceAccountId == accountId)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Skip(offset).Take(20).ToListAsync();
        return Results.Ok(rows.Select(View));
    }

    public static int Offset(int? page) => page is null ? 0 : page is >= 1 and <= 100_000 ? (page.Value - 1) * 20 : -1;

    private static async Task<IResult> Queue(int? page, BankDb db)
    {
        var offset = Offset(page);
        if (offset < 0) return Error(400, "Neveljavna stran.");
        var rows = await (from t in db.Transfers
                          join source in db.Accounts on t.SourceAccountId equals source.Id
                          join target in db.Accounts on t.TargetAccountId equals target.Id
                          join sender in db.Users on source.OwnerId equals sender.Id
                          join recipient in db.Users on target.OwnerId equals recipient.Id
                          where t.Status == Pending
                          orderby t.CreatedAt, t.Id
                          select new { t.Id, t.Amount, t.Purpose, t.CreatedAt, Sender = sender.Name,
                              Recipient = recipient.Name, Source = source.Number, Target = target.Number })
            .Skip(offset).Take(20).ToListAsync();
        return Results.Ok(rows.Select(x => new { x.Id, amount = Money(x.Amount), x.Purpose, x.CreatedAt,
            x.Sender, x.Recipient, x.Source, x.Target }));
    }

    private static Task<IResult> Submit(TransferInput input, HttpRequest request, ClaimsPrincipal user, BankDb db)
    {
        var purpose = input.Purpose?.Trim();
        var target = input.TargetAccount?.Trim().ToUpperInvariant();
        if (!Guid.TryParse(request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty
            || !Amount(input.Amount, out var amount) || amount <= 0 || purpose is null or "" || purpose.Length > 140
            || target is null or "" || target.Length > 32)
            return Task.FromResult(Error(400, "Vnesite prejemnika, veljaven znesek, namen in ključ zahteve."));
        return Run(db, async () =>
        {
            if (!await HasAccess(db, user, "CLIENT")) return Results.Unauthorized();
            var owner = Id(user);
            var destination = await db.Accounts.SingleOrDefaultAsync(a => a.Number == target);
            var previous = await Previous(db, owner, key);
            if (previous is not null)
                return previous.SourceAccountId == input.SourceAccountId && previous.TargetAccountId == destination?.Id
                    && previous.Amount == amount && previous.Purpose == purpose
                    ? Results.Ok(View(previous)) : KeyConflict();
            if (destination is null) return Error(400, "Prejemnikov račun ne obstaja.");
            return await Create(db, owner, key, input.SourceAccountId, destination.Id, amount, purpose);
        });
    }

    public static Task<Transfer?> Previous(BankDb db, Guid user, Guid key) =>
        db.Transfers.SingleOrDefaultAsync(t => t.RequestedById == user && t.RequestKey == key);

    public static IResult KeyConflict() => Error(409, "Ključ zahteve je že uporabljen za drugo nakazilo.");

    public static async Task<IResult> Create(BankDb db, Guid owner, Guid key, Guid sourceId, Guid targetId,
        decimal amount, string purpose, Action<Transfer>? configure = null)
    {
        var source = await db.Accounts.SingleOrDefaultAsync(x => x.Id == sourceId && x.OwnerId == owner);
        if (source is null) return Results.NotFound();
        if (sourceId == targetId) return Error(400, "Izberite drug ciljni račun.");
        var target = await db.Accounts.SingleOrDefaultAsync(x => x.Id == targetId);
        if (target is null || !await IsActiveClient(db, target.OwnerId)) return Error(400, "Prejemnikov račun ni na voljo.");
        if (source.Balance < amount) return Error(400, "Premalo sredstev.");
        if (amount <= 500 && target.Balance > MaxAmount - amount) return Error(400, "Znesek presega dovoljeno stanje prejemnika.");

        var transfer = new Transfer
        {
            SourceAccountId = source.Id, TargetAccountId = target.Id, Amount = amount,
            Purpose = purpose, RequestedById = owner, RequestKey = key, Status = Pending
        };
        configure?.Invoke(transfer);
        if (amount <= 500) Execute(transfer, source, target, DateTime.UtcNow);
        db.Transfers.Add(transfer);
        return Results.Json(View(transfer), statusCode: 201);
    }

    private static Task<IResult> Decide(Guid id, TransferDecision input, ClaimsPrincipal user, BankDb db)
    {
        var reason = input.Reason?.Trim();
        if (input.Approve is null || (input.Approve == false && (reason is null or "" || reason.Length > 500)))
            return Task.FromResult(Error(400, "Pri zavrnitvi je obvezen razlog do 500 znakov."));
        return Run(db, async () =>
        {
            if (!await HasAccess(db, user, "EMPLOYEE")) return Results.Unauthorized();
            var transfer = await db.Transfers.FindAsync(id);
            if (transfer is null) return Results.NotFound();
            if (transfer.Status != Pending) return Error(409, "Nakazilo je že obravnavano.");

            var now = DateTime.UtcNow;
            transfer.ReviewedById = Id(user);
            transfer.ReviewedAt = now;
            if (input.Approve == false)
            {
                transfer.Status = Rejected;
                transfer.ReviewReason = reason;
            }
            else
            {
                var source = (await db.Accounts.FindAsync(transfer.SourceAccountId))!;
                var target = (await db.Accounts.FindAsync(transfer.TargetAccountId))!;
                var failure = await ApprovalFailure(db, source, target, transfer.Amount);
                if (failure is null) Execute(transfer, source, target, now);
                else
                {
                    transfer.Status = Failed;
                    transfer.ReviewReason = failure;
                }
            }
            return Results.Ok(new { transfer.Id, transfer.Status });
        });
    }

    private static async Task<string?> ApprovalFailure(BankDb db, BankAccount source, BankAccount target, decimal amount)
    {
        if (!await IsActiveClient(db, source.OwnerId) || !await IsActiveClient(db, target.OwnerId))
            return "Uporabniški dostop ni aktiven.";
        if (source.Balance < amount) return "Premalo sredstev ob odobritvi.";
        return target.Balance > MaxAmount - amount ? "Znesek presega dovoljeno stanje prejemnika." : null;
    }

    public static Task<bool> IsActiveClient(BankDb db, Guid id) =>
        db.Users.AnyAsync(u => u.Id == id && u.Active && u.Role == "CLIENT");

    private static void Execute(Transfer transfer, BankAccount source, BankAccount target, DateTime now)
    {
        source.Balance -= transfer.Amount;
        target.Balance += transfer.Amount;
        transfer.Status = Executed;
        transfer.ExecutedAt = now;
    }

    public static object View(Transfer t) => new { t.Id, amount = Money(t.Amount), t.Purpose,
        t.Status, t.CreatedAt, t.ExecutedAt, reason = t.ReviewReason };
}

internal record TransferInput(Guid SourceAccountId, string? TargetAccount, string? Amount, string? Purpose);
internal record TransferDecision(bool? Approve, string? Reason);
