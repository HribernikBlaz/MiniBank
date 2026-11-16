using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniBank.Api;
using Xunit;

namespace MiniBank.Tests;

public class MvpTests
{
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
