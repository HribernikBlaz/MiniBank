using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniBank.Api;
using Xunit;

namespace MiniBank.Tests;

public class MvpTests
{
    [Fact]
    public async Task Obligation_payment_rejection_failure_retry_and_approval()
    {
        using var app = new BankFactory();
        using var creditor = app.Start();
        using var debtor = app.CreateClient();
        using var employee = app.CreateClient();
        await app.Login(creditor);
        var debtorId = await LoginId(app, debtor, "empty");
        await app.Login(employee, "employee");
        var group = await CreatedId(await creditor.PostAsJsonAsync("/api/groups", new { name = "Izlet" }));
        var root = $"/api/groups/{group}";
        (await creditor.PostAsJsonAsync(root + "/invite", new { email = "empty@minibank.test" })).EnsureSuccessStatusCode();
        (await debtor.PostAsJsonAsync(root + "/invitation", new { accept = true })).EnsureSuccessStatusCode();
        await Edit(app, async db => (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance = 1000);

        var input = new
        {
            title = "Zasebni strošek", category = "TRANSPORT", date = DateOnly.FromDateTime(DateTime.UtcNow),
            total = "680.00", accountId = app.AccountId, equal = false,
            shares = new[] { new { userId = app.AnaId, amount = "30.00" }, new { userId = debtorId, amount = "650.00" } }
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await creditor.PostAsJsonAsync(root + "/expenses", input with { total = "680.01" })).StatusCode);
        var expense = await CreatedId(await creditor.PostAsJsonAsync(root + "/expenses", input));
        (await creditor.PostAsJsonAsync($"{root}/expenses/{expense}/submit", new { })).EnsureSuccessStatusCode();
        var ledgerPath = root + "/obligations";
        Assert.Empty((await debtor.GetFromJsonAsync<JsonElement>(ledgerPath)).GetProperty("items").EnumerateArray());
        (await debtor.PostAsJsonAsync($"{root}/expenses/{expense}/decision", new { accept = true })).EnsureSuccessStatusCode();
        var ledger = await debtor.GetFromJsonAsync<JsonElement>(ledgerPath);
        var debtId = ledger.GetProperty("items")[0].GetProperty("id").GetGuid();
        var payment = $"{ledgerPath}/{debtId}/pay";
        var body = new { sourceAccountId = app.OtherAccountId };
        Assert.Equal(HttpStatusCode.NotFound, (await Send(creditor, payment, body, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(debtor, payment, new { sourceAccountId = app.AccountId }, Guid.NewGuid())).StatusCode);

        var key = Guid.NewGuid();
        var pending = await CreatedId(await Send(debtor, payment, body, key));
        Assert.Equal(HttpStatusCode.OK, (await Send(debtor, payment, body, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(debtor, payment, body, Guid.NewGuid())).StatusCode);
        var queue = await employee.GetStringAsync("/api/review/transfers");
        Assert.DoesNotContain("Zasebni strošek", queue);
        Assert.DoesNotContain("balance", queue, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("650.00", (await creditor.GetFromJsonAsync<JsonElement>(ledgerPath)).GetProperty("receivable").GetString());
        await Edit(app, async db => Assert.Equal(1000m, (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance));
        string Review(Guid id) => $"/api/review/transfers/{id}/decision";
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync(Review(pending), new { approve = false })).StatusCode);
        (await employee.PostAsJsonAsync(Review(pending), new { approve = false, reason = "Interni razlog" })).EnsureSuccessStatusCode();
        var open = await creditor.GetStringAsync(ledgerPath);
        Assert.Contains("OPEN", open);
        Assert.DoesNotContain("Interni razlog", open);

        pending = await CreatedId(await Send(debtor, payment, body, Guid.NewGuid()));
        await Edit(app, async db => (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance = 600);
        var failed = await employee.PostAsJsonAsync(Review(pending), new { approve = true });
        Assert.Equal("FAILED", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal("650.00", (await creditor.GetFromJsonAsync<JsonElement>(ledgerPath)).GetProperty("receivable").GetString());

        await Edit(app, async db => (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance = 900);
        pending = await CreatedId(await Send(debtor, payment, body, Guid.NewGuid()));
        (await employee.PostAsJsonAsync(Review(pending), new { approve = true })).EnsureSuccessStatusCode();
        ledger = await creditor.GetFromJsonAsync<JsonElement>(ledgerPath);
        Assert.Equal("0.00", ledger.GetProperty("receivable").GetString());
        Assert.Equal("SETTLED", ledger.GetProperty("items")[0].GetProperty("status").GetString());
        await Edit(app, async db =>
        {
            Assert.Equal(250m, (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance);
            Assert.Equal(850m, (await db.Accounts.FindAsync(app.AccountId))!.Balance);
        });

        var dinner = await CreatedId(await creditor.PostAsJsonAsync(root + "/expenses",
            input with { title = "Večerja", total = "60.00", equal = true }));
        (await creditor.PostAsJsonAsync($"{root}/expenses/{dinner}/submit", new { })).EnsureSuccessStatusCode();
        (await debtor.PostAsJsonAsync($"{root}/expenses/{dinner}/decision", new { accept = true })).EnsureSuccessStatusCode();
        ledger = await debtor.GetFromJsonAsync<JsonElement>(ledgerPath);
        var openDebt = ledger.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("status").GetString() == "OPEN");
        payment = $"{ledgerPath}/{openDebt.GetProperty("id").GetGuid()}/pay";
        if (Environment.GetEnvironmentVariable("MINIBANK_TEST_POSTGRES") is not null)
        {
            using var secondSession = app.CreateClient();
            await app.Login(secondSession, "empty");
            var responses = await Task.WhenAll(Send(debtor, payment, body, Guid.NewGuid()),
                Send(secondSession, payment, body, Guid.NewGuid()));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        else Assert.Equal(HttpStatusCode.Created, (await Send(debtor, payment, body, Guid.NewGuid())).StatusCode);
        await Edit(app, async db =>
        {
            Assert.Equal(220m, (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance);
            Assert.Equal(880m, (await db.Accounts.FindAsync(app.AccountId))!.Balance);
        });
    }

    [Fact]
    public async Task Administration_protects_last_admin_and_revokes_access_without_changing_accounts()
    {
        using var app = new BankFactory();
        using var client = app.Start();
        using var administrator = app.CreateClient();
        using var created = app.CreateClient();
        await app.Login(client);
        var administratorId = await LoginId(app, administrator, "admin");
        const string path = "/api/admin/users";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsJsonAsync($"{path}/{administratorId}/access",
            new { role = "ADMIN", active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsJsonAsync($"{path}/{app.AnaId}/access",
            new { role = "EMPLOYEE", active = true })).StatusCode);

        var access = new { name = "Nov uporabnik", email = " NOV@minibank.test ", role = "CLIENT", password = "Local-Test-Password" };
        await CreatedId(await administrator.PostAsJsonAsync(path, access));
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsJsonAsync(path, access)).StatusCode);
        await app.Login(created, "nov");
        Assert.Empty((await created.GetFromJsonAsync<JsonElement>("/api/accounts")).EnumerateArray());
        Assert.DoesNotContain("password", await administrator.GetStringAsync(path), StringComparison.OrdinalIgnoreCase);

        (await administrator.PostAsJsonAsync($"{path}/{app.AnaId}/access", new { role = "CLIENT", active = false })).EnsureSuccessStatusCode();
        (await administrator.PostAsJsonAsync($"{path}/{app.AnaId}/access", new { role = "CLIENT", active = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
        await app.Login(client);
        Assert.Equal("200.00", (await client.GetFromJsonAsync<JsonElement>("/api/accounts"))[0].GetProperty("balance").GetString());
    }

    [Fact]
    public async Task Group_invitation_draft_dispute_and_confirmation_work_together()
    {
        using var app = new BankFactory();
        using var payer = app.Start();
        using var member = app.CreateClient();
        using var third = app.CreateClient();
        var thirdUser = new AppUser { Email = "cene@minibank.test", Name = "Cene" };
        thirdUser.PasswordHash = new PasswordHasher<AppUser>().HashPassword(thirdUser, "Local-Test-Password");
        await Edit(app, db => { db.Users.Add(thirdUser); return Task.CompletedTask; });
        await app.Login(payer);
        var memberId = await LoginId(app, member, "empty");
        await app.Login(third, "cene");
        var group = await CreatedId(await payer.PostAsJsonAsync("/api/groups", new { name = "Večerja" }));
        var root = $"/api/groups/{group}";
        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync(root + "/invite", new { email = "empty@minibank.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync(root + "/invite", new { email = "empty@minibank.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(root + "/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsJsonAsync(root + "/invitation", new { accept = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(root + "/invite", new { email = "cene@minibank.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync(root + "/invite", new { email = "cene@minibank.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await third.PostAsJsonAsync(root + "/invitation", new { accept = true })).StatusCode);
        var expense = await CreatedId(await payer.PostAsJsonAsync(root + "/expenses", new
        {
            title = "Večerja", category = "FOOD", date = DateOnly.FromDateTime(DateTime.UtcNow),
            total = "10.00", accountId = app.AccountId, equal = true,
            shares = new[] { new { userId = thirdUser.Id }, new { userId = memberId }, new { userId = app.AnaId } }
        }));
        Assert.Empty((await member.GetFromJsonAsync<JsonElement>(root + "/expenses")).EnumerateArray());
        var path = $"{root}/expenses/{expense}";
        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync(path + "/submit", new { })).StatusCode);
        var proposals = await member.GetFromJsonAsync<JsonElement>(root + "/expenses");
        var shares = proposals[0].GetProperty("shares").EnumerateArray().ToList();
        var first = new[] { app.AnaId, memberId, thirdUser.Id }.Min();
        Assert.All(shares, share => Assert.Equal(share.GetProperty("userId").GetGuid() == first ? "3.34" : "3.33",
            share.GetProperty("amount").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync(path + "/decision", new { accept = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsJsonAsync(path + "/decision", new { accept = false, reason = "Preveri znesek" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await third.PostAsJsonAsync(path + "/decision", new { accept = true })).StatusCode);
        await Edit(app, async db => Assert.Empty(await db.Obligations.ToListAsync()));

        var revised = await CreatedId(await payer.PostAsJsonAsync(path + "/revise", new { }));
        var next = $"{root}/expenses/{revised}";
        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync(next + "/submit", new { })).StatusCode);
        if (Environment.GetEnvironmentVariable("MINIBANK_TEST_POSTGRES") is not null)
        {
            var responses = await Task.WhenAll(member.PostAsJsonAsync(next + "/decision", new { accept = true }),
                third.PostAsJsonAsync(next + "/decision", new { accept = true }));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsJsonAsync(next + "/decision", new { accept = true })).StatusCode);
            await Edit(app, async db => Assert.Empty(await db.Obligations.ToListAsync()));
            Assert.Equal(HttpStatusCode.NoContent, (await third.PostAsJsonAsync(next + "/decision", new { accept = true })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsJsonAsync(next + "/decision", new { accept = true })).StatusCode);
        await Edit(app, async db =>
        {
            Assert.Equal(2, await db.Obligations.CountAsync());
            Assert.Equal("Preveri znesek", (await db.Shares.FindAsync(expense, memberId))!.Reason);
            Assert.Equal("SUPERSEDED", (await db.Expenses.FindAsync(expense))!.Status);
            Assert.Equal("CONFIRMED", (await db.Expenses.FindAsync(revised))!.Status);
            Assert.Equal(200m, (await db.Accounts.FindAsync(app.AccountId))!.Balance);
        });
    }

    [Fact]
    public async Task Transfer_replay_approval_threshold_and_audit()
    {
        using var app = new BankFactory();
        using var client = app.Start();
        using var employee = app.CreateClient();
        await Edit(app, async db => (await db.Accounts.FindAsync(app.AccountId))!.Balance = 1200);
        await app.Login(client);
        await app.Login(employee, "employee");
        var key = Guid.NewGuid();
        var first = await Send(client, "/api/transfers", Transfer(app.AccountId, "500.00"), key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var replay = await Send(client, "/api/transfers", Transfer(app.AccountId, "500.00"), key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, "/api/transfers", Transfer(app.AccountId, "499"), key)).StatusCode);

        var pending = await Send(client, "/api/transfers", Transfer(app.AccountId, "500.01"), Guid.NewGuid());
        var id = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Edit(app, async db => Assert.Equal(700m, (await db.Accounts.FindAsync(app.AccountId))!.Balance));
        var queue = await employee.GetFromJsonAsync<JsonElement>("/api/review/transfers");
        Assert.Contains(queue.EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
        var path = $"/api/review/transfers/{id}/decision";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employee.PostAsJsonAsync(path, new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await employee.PostAsJsonAsync(path, new { approve = true })).StatusCode);
        var reviewed = await employee.GetFromJsonAsync<JsonElement>("/api/review/transfers?reviewed=true");
        Assert.Single(reviewed.EnumerateArray());
        Assert.Equal(id, reviewed[0].GetProperty("id").GetGuid());
        await Edit(app, async db =>
        {
            Assert.Equal(199.99m, (await db.Accounts.FindAsync(app.AccountId))!.Balance);
            Assert.Equal(1500.01m, (await db.Accounts.FindAsync(app.OtherAccountId))!.Balance);
            var transfer = await db.Transfers.FindAsync(id);
            Assert.NotNull(transfer!.ReviewedById);
            Assert.NotNull(transfer.ReviewedAt);
        });
    }

    [Fact]
    public async Task Concurrent_payments_cannot_overdraw_PostgreSql()
    {
        if (Environment.GetEnvironmentVariable("MINIBANK_TEST_POSTGRES") is null) return;
        using var app = new BankFactory();
        using var first = app.Start();
        using var second = app.CreateClient();
        await Edit(app, async db => (await db.Accounts.FindAsync(app.AccountId))!.Balance = 100);
        await app.Login(first);
        await app.Login(second);
        var replies = await Task.WhenAll(
            Send(first, "/api/transfers", Transfer(app.AccountId, "80"), Guid.NewGuid()),
            Send(second, "/api/transfers", Transfer(app.AccountId, "80"), Guid.NewGuid()));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.BadRequest);
        await Edit(app, async db => Assert.Equal(20m, (await db.Accounts.FindAsync(app.AccountId))!.Balance));
    }

    private static object Transfer(Guid id, string amount) =>
        new { sourceAccountId = id, targetAccount = "MB-BOR", amount, purpose = "Preizkus" };

    private static async Task<Guid> LoginId(BankFactory app, HttpClient client, string name)
    {
        var result = await app.Login(client, name);
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatedId(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task Edit(BankFactory app, Func<BankDb, Task> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        await action(db);
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, Guid key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }
}
