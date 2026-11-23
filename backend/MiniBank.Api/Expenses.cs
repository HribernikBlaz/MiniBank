using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static MiniBank.Api.MvpWrite;

namespace MiniBank.Api;

internal static class Expenses
{
    public const string Draft = "DRAFT";
    public const string Pending = "PENDING_CONFIRMATION";
    public const string Disputed = "DISPUTED";
    public const string Confirmed = "CONFIRMED";
    public const string Accepted = "ACCEPTED";
    public const string Cancelled = "CANCELLED";
    public const string Superseded = "SUPERSEDED";
    private static readonly TimeZoneInfo Ljubljana = TimeZoneInfo.FindSystemTimeZoneById("Europe/Ljubljana");

    public static void MapExpenses(this WebApplication app)
    {
        var expense = app.MapGroup("/api/groups/{groupId:guid}/expenses").RequireAuthorization(p => p.RequireRole("CLIENT"));
        expense.MapGet("", List);
        expense.MapPost("", Create);
        expense.MapPost("/{id:guid}/edit", Edit);
        expense.MapPost("/{id:guid}/submit", Submit);
        expense.MapPost("/{id:guid}/decision", Decide);
        expense.MapPost("/{id:guid}/revise", Revise);
        expense.MapPost("/{id:guid}/cancel", Cancel);
    }

    private static async Task<IResult> List(Guid groupId, ClaimsPrincipal user, BankDb db)
    {
        var owner = Id(user);
        if (!await Groups.IsMember(db, groupId, owner)) return Results.NotFound();
        var expenses = await db.Expenses.AsNoTracking().Include(x => x.Shares)
            .Where(x => x.GroupId == groupId && (x.PayerId == owner
                || x.Status != Draft && x.Shares.Any(s => s.UserId == x.PayerId && s.Decision == Accepted)))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Version).ThenBy(x => x.Id).ToListAsync();
        return Results.Ok(expenses.Select(x => new
        {
            x.Id, x.Title, x.Category, x.Date, x.PayerId, accountId = x.PayerId == owner ? (Guid?)x.AccountId : null,
            x.Version, x.PreviousId, x.Status,
            total = Money(x.Total),
            shares = x.Shares.Select(s => new { s.UserId, amount = Money(s.Value), s.Decision, s.Reason, s.DecidedAt })
        }));
    }

    private static Task<IResult> Create(Guid groupId, ExpenseInput input, ClaimsPrincipal user, BankDb db) =>
        Write(db, user, groupId, async owner =>
        {
            var expense = new Expense { GroupId = groupId, PayerId = owner };
            var error = await Apply(db, expense, input);
            if (error is not null) return error;
            db.Expenses.Add(expense);
            return Results.Json(new { expense.Id }, statusCode: 201);
        });

    private static Task<IResult> Edit(Guid groupId, Guid id, ExpenseInput input, ClaimsPrincipal user, BankDb db) =>
        Write(db, user, groupId, async owner =>
        {
            var expense = await Find(db, groupId, id);
            if (expense is null || expense.PayerId != owner) return Results.NotFound();
            if (expense.Status != Draft) return Error(409, "Urejate lahko samo osnutek.");
            var error = await Apply(db, expense, input);
            return error ?? Results.NoContent();
        });

    private static Task<IResult> Submit(Guid groupId, Guid id, ClaimsPrincipal user, BankDb db) =>
        Write(db, user, groupId, async owner =>
        {
            var expense = await Find(db, groupId, id);
            if (expense is null || expense.PayerId != owner) return Results.NotFound();
            if (expense.Status != Draft) return Error(409, "Predlog je že oddan.");
            if (!await ActiveParticipants(db, groupId, expense.Shares.Select(x => x.UserId).ToArray()))
                return Error(409, "Pri oddaji morajo biti vsi udeleženci aktivni.");
            expense.Status = Pending;
            var ownShare = expense.Shares.Single(x => x.UserId == owner);
            ownShare.Decision = Accepted;
            ownShare.DecidedAt = DateTime.UtcNow;
            return Results.NoContent();
        });

    private static Task<IResult> Decide(Guid groupId, Guid id, ShareDecision input, ClaimsPrincipal user, BankDb db) =>
        Write(db, user, groupId, async owner =>
        {
            if (input.Accept is null) return Error(400, "Izberite potrditev ali ugovor.");
            var reason = input.Reason?.Trim();
            if (input.Accept == false && (reason is null or "" || reason.Length > 500))
                return Error(400, "Ugovor zahteva razlog do 500 znakov.");

            var expense = await Find(db, groupId, id);
            var share = expense?.Shares.SingleOrDefault(x => x.UserId == owner);
            if (expense is null || expense.Status == Draft || share is null || expense.PayerId == owner)
                return Results.NotFound();
            var decision = input.Accept.Value ? Accepted : Disputed;
            if (share.Decision == decision && (decision == Accepted || reason == share.Reason)) return Results.NoContent();
            if (expense.Status != Pending || share.Decision != "PENDING") return Error(409, "Predlog ali delež je že obravnavan.");

            share.Decision = decision;
            share.DecidedAt = DateTime.UtcNow;
            if (decision == Disputed)
            {
                share.Reason = reason;
                expense.Status = Disputed;
            }
            else
            {
                // Vsaka odločitev posodobi isto vrstico predloga, tudi če potrjevanje še ni končano.
                db.Entry(expense).Property(x => x.Status).IsModified = true;
                if (expense.Shares.All(s => s.Decision == Accepted))
                {
                    expense.Status = Confirmed;
                    foreach (var participant in expense.Shares.Where(s => s.UserId != expense.PayerId && s.Value > 0))
                        db.Obligations.Add(new Obligation
                        {
                            ExpenseId = id, DebtorId = participant.UserId,
                            CreditorId = expense.PayerId, AccountId = expense.AccountId, Amount = participant.Value
                        });
                }
            }
            return Results.NoContent();
        });

    private static Task<IResult> Revise(Guid groupId, Guid id, ClaimsPrincipal user, BankDb db) =>
        Write(db, user, groupId, async owner =>
        {
            var prior = await Find(db, groupId, id);
            if (prior is null || prior.PayerId != owner) return Results.NotFound();
            if (prior.Status != Disputed || await db.Expenses.AnyAsync(x => x.PreviousId == id))
                return Error(409, "Samo sporni predlog brez naslednika lahko revidirate.");
            var next = new Expense
            {
                GroupId = groupId, PayerId = owner, AccountId = prior.AccountId,
                Version = prior.Version + 1, PreviousId = id, Title = prior.Title, Category = prior.Category,
                Date = prior.Date, Total = prior.Total,
                Shares = prior.Shares.Select(s => new ExpenseShare { UserId = s.UserId, Value = s.Value }).ToList()
            };
            db.Expenses.Add(next);
            prior.Status = Superseded;
            return Results.Json(new { next.Id }, statusCode: 201);
        });

    private static Task<IResult> Cancel(Guid groupId, Guid id, ClaimsPrincipal user, BankDb db) =>
        Write(db, user, groupId, async owner =>
        {
            var expense = await Find(db, groupId, id);
            if (expense is null || expense.PayerId != owner) return Results.NotFound();
            if (expense.Status is not (Draft or Pending or Disputed))
                return Error(409, "Tega predloga ni več mogoče preklicati.");
            expense.Status = Cancelled;
            return Results.NoContent();
        });

    private static Task<IResult> Write(BankDb db, ClaimsPrincipal user, Guid group, Func<Guid, Task<IResult>> action) =>
        Run(db, async () =>
        {
            if (!await HasAccess(db, user, "CLIENT")) return Results.Unauthorized();
            var owner = Id(user);
            return await Groups.IsMember(db, group, owner) ? await action(owner) : Results.NotFound();
        });

    private static Task<Expense?> Find(BankDb db, Guid group, Guid id) =>
        db.Expenses.Include(x => x.Shares).SingleOrDefaultAsync(x => x.Id == id && x.GroupId == group);

    private static async Task<bool> ActiveParticipants(BankDb db, Guid groupId, Guid[] ids) =>
        await (from member in db.Memberships
               join user in db.Users on member.UserId equals user.Id
               where member.GroupId == groupId && member.Status == Groups.Member && user.Active && user.Role == "CLIENT"
                  && ids.Contains(member.UserId)
               select member).CountAsync() == ids.Length;

    private static async Task<IResult?> Apply(BankDb db, Expense expense, ExpenseInput input)
    {
        var title = input.Title?.Trim();
        var category = input.Category?.Trim();
        var people = input.Shares;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ljubljana));
        if (title is null or "" || title.Length > 140
            || category is not ("FOOD" or "TRANSPORT" or "ACCOMMODATION" or "HOUSEHOLD" or "OTHER")
            || input.Date == default || input.Date > today
            || !Amount(input.Total, out var total) || total <= 0
            || people is null || people.Length < 2 || people.Any(x => x is null))
            return Error(400, "Vnesite veljaven strošek, datum in vsaj dva udeleženca.");
        var ids = people.Select(x => x.UserId).ToArray();
        if (!ids.Contains(expense.PayerId) || ids.Distinct().Count() != ids.Length
            || !await ActiveParticipants(db, expense.GroupId, ids))
            return Error(400, "Izberite različne aktivne člane, tudi plačnika.");
        if (!await db.Accounts.AnyAsync(x => x.Id == input.AccountId && x.OwnerId == expense.PayerId))
            return Error(400, "Izberite svoj račun za povračilo.");

        var values = new Dictionary<Guid, decimal>();
        if (input.Equal)
        {
            long cents = (long)(total * 100);
            var ordered = ids.OrderBy(id => id).ToArray();
            for (var index = 0; index < ordered.Length; index++)
                values.Add(ordered[index], (cents / ordered.Length + (index < cents % ordered.Length ? 1 : 0)) / 100m);
        }
        else
        {
            foreach (var person in people)
            {
                if (!Amount(person.Amount, out var value)) return Error(400, "Deleži morajo biti nenegativni z največ dvema decimalkama.");
                values.Add(person.UserId, value);
            }
        }
        if (values.Values.Sum() != total || !values.Any(x => x.Key != expense.PayerId && x.Value > 0))
            return Error(400, "Vsota deležev mora ustrezati strošku in vsaj en drug član mora imeti pozitiven delež.");

        expense.Title = title;
        expense.Category = category;
        expense.Total = total;
        expense.Date = input.Date;
        expense.AccountId = input.AccountId;
        foreach (var share in expense.Shares.ToArray())
        {
            if (values.Remove(share.UserId, out var value)) share.Value = value;
            else
            {
                expense.Shares.Remove(share);
                db.Shares.Remove(share);
            }
        }
        foreach (var (member, value) in values)
            expense.Shares.Add(new ExpenseShare { ExpenseId = expense.Id, UserId = member, Value = value });
        return null;
    }
}

internal record ExpenseInput(string? Title, string? Category, DateOnly Date, string? Total, Guid AccountId, bool Equal, ShareInput[]? Shares);
internal record ShareInput(Guid UserId, string? Amount);
internal record ShareDecision(bool? Accept, string? Reason);
