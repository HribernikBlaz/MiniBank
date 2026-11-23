using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniBank.Api;
using Xunit;

namespace MiniBank.Tests;

public class ValidationTests
{
    private static Task<HttpResponseMessage> Send(HttpClient client, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key.ToString());
        return client.SendAsync(request);
    }

    private static object Transfer(Guid source, string target, string amount) =>
        new { sourceAccountId = source, targetAccount = target, amount, purpose = "Test" };

    private static async Task<decimal> Balance(BankFactory app, Guid accountId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();
        return (await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId)).Balance;
    }

    [Fact]
    public async Task Transfer_up_to_500_executes_immediately_and_changes_balances()
    {
        using var app = new BankFactory();
        using var client = app.Start();
        await app.Login(client);
        var response = await Send(client, Transfer(app.AccountId, "MB-BOR", "30.00"), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("EXECUTED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(170m, await Balance(app, app.AccountId));
        Assert.Equal(530m, await Balance(app, app.OtherAccountId));
    }

    [Fact]
    public async Task Transfer_with_insufficient_funds_is_rejected_without_moving_money()
    {
        using var app = new BankFactory();
        using var client = app.Start();
        await app.Login(client);
        var response = await Send(client, Transfer(app.AccountId, "MB-BOR", "250.00"), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(200m, await Balance(app, app.AccountId));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("10.999")]
    public async Task Transfer_with_invalid_amount_is_rejected(string amount)
    {
        using var app = new BankFactory();
        using var client = app.Start();
        await app.Login(client);
        var response = await Send(client, Transfer(app.AccountId, "MB-BOR", amount), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(200m, await Balance(app, app.AccountId));
    }

    [Theory]
    [InlineData("MB-ANA")]
    [InlineData("MB-NIKDAR")]
    public async Task Transfer_to_own_or_unknown_account_is_rejected(string target)
    {
        using var app = new BankFactory();
        using var client = app.Start();
        await app.Login(client);
        var response = await Send(client, Transfer(app.AccountId, target, "10.00"), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(200m, await Balance(app, app.AccountId));
    }

    [Fact]
    public async Task Transfer_without_idempotency_key_is_rejected()
    {
        using var app = new BankFactory();
        using var client = app.Start();
        await app.Login(client);
        var response = await Send(client, Transfer(app.AccountId, "MB-BOR", "10.00"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_requires_login_and_client_role()
    {
        using var app = new BankFactory();
        using var anonymous = app.Start();
        await BankFactory.Csrf(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, Transfer(app.AccountId, "MB-BOR", "10.00"), Guid.NewGuid())).StatusCode);
        using var employee = app.CreateClient();
        await app.Login(employee, "employee");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, Transfer(app.AccountId, "MB-BOR", "10.00"), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Rejecting_a_transfer_requires_a_reason()
    {
        using var app = new BankFactory();
        using var client = app.Start();
        using var employee = app.CreateClient();
        await app.Login(employee, "employee");
        var queue = await employee.GetFromJsonAsync<JsonElement>("/api/review/transfers");
        var id = queue[0].GetProperty("id").GetGuid();
        var path = $"/api/review/transfers/{id}/decision";
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync(path, new { approve = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employee.PostAsJsonAsync(path, new { approve = false, reason = "Sumljivo nakazilo" })).StatusCode);
    }

    [Fact]
    public async Task Admin_validates_new_users()
    {
        using var app = new BankFactory();
        using var admin = app.Start();
        await app.Login(admin, "admin");
        var valid = new { name = "Nov", email = "nov@minibank.test", password = "DolgoGeslo-12345", role = "CLIENT" };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/users", valid with { password = "kratko" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/users", valid with { role = "SEF" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/users", valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/users", valid)).StatusCode);
    }
}
