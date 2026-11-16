using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static MiniBank.Api.MvpWrite;

namespace MiniBank.Api;

internal static class Obligations
{
    public static void MapObligations(this WebApplication app)
    {
        var debts = app.MapGroup("/api/groups/{groupId:guid}/obligations").RequireAuthorization(p => p.RequireRole("CLIENT"));
        debts.MapGet("", List);
        debts.MapPost("/{id:guid}/pay", Pay);
    }

    private static async Task<IResult> List(Guid groupId, ClaimsPrincipal user, BankDb db)
    {
        var owner = Id(user);
        if (!await Groups.IsMember(db, groupId, owner)) return Results.NotFound();
        var rows = await (from debt in db.Obligations
                          join expense in db.Expenses on debt.ExpenseId equals expense.Id
                          join payer in db.Users on debt.DebtorId equals payer.Id
                          join recipient in db.Users on debt.CreditorId equals recipient.Id
                          where expense.GroupId == groupId && (debt.DebtorId == owner || debt.CreditorId == owner)
                          orderby expense.Date descending, debt.Id
                          select new
                          {
                              debt.Id, expense.Title, debt.Amount, Debtor = payer.Name, Creditor = recipient.Name,
                              Owes = debt.DebtorId == owner,
                              Settled = db.Transfers.Any(t => t.ObligationId == debt.Id && t.Status == Transfers.Executed),
                              Pending = db.Transfers.Any(t => t.ObligationId == debt.Id && t.Status == Transfers.Pending)
                          }).ToListAsync();
        var payable = rows.Where(r => !r.Settled && r.Owes).Sum(r => r.Amount);
        var receivable = rows.Where(r => !r.Settled && !r.Owes).Sum(r => r.Amount);
        return Results.Ok(new
        {
            payable = Money(payable), receivable = Money(receivable), net = Money(receivable - payable),
            items = rows.Select(r => new { r.Id, r.Title, r.Debtor, r.Creditor, r.Owes, amount = Money(r.Amount),
                status = r.Settled ? "SETTLED" : r.Pending ? "PAYMENT_PENDING" : "OPEN" })
        });
    }

    private static Task<IResult> Pay(Guid groupId, Guid id, SettlementInput input, HttpRequest request, ClaimsPrincipal user, BankDb db)
    {
        if (!Guid.TryParse(request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
            return Task.FromResult(Error(400, "Manjka veljaven ključ zahteve."));
        return Run(db, async () =>
        {
            if (!await HasAccess(db, user, "CLIENT")) return Results.Unauthorized();
            var owner = Id(user);
            if (!await Groups.IsMember(db, groupId, owner)) return Results.NotFound();
            var debt = await (from o in db.Obligations
                              join e in db.Expenses on o.ExpenseId equals e.Id
                              where o.Id == id && e.GroupId == groupId && o.DebtorId == owner
                              select o).SingleOrDefaultAsync();
            if (debt is null) return Results.NotFound();
            var previous = await Transfers.Previous(db, owner, key);
            if (previous is not null)
                return previous.ObligationId == id && previous.SourceAccountId == input.SourceAccountId
                    ? Results.Ok(Transfers.View(previous)) : Transfers.KeyConflict();
            if (await db.Transfers.AnyAsync(t => t.ObligationId == id && (t.Status == Transfers.Pending || t.Status == Transfers.Executed)))
                return Error(409, "Plačilo že čaka na odobritev ali je dolg poravnan.");
            // Čakalna vrsta uslužbenca ne razkrije naziva ali vsebine skupinskega stroška.
            return await Transfers.Create(db, owner, key, input.SourceAccountId, debt.AccountId, debt.Amount,
                "Poravnava skupinske obveznosti", t => t.ObligationId = id);
        });
    }
}

internal record SettlementInput(Guid SourceAccountId);
