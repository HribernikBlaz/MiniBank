using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static MiniBank.Api.MvpWrite;

namespace MiniBank.Api;

internal static class Groups
{
    public const string Invited = "INVITED";
    public const string Member = "ACTIVE";
    public const string Declined = "DECLINED";

    public static void MapGroups(this WebApplication app)
    {
        var group = app.MapGroup("/api/groups").RequireAuthorization(p => p.RequireRole("CLIENT"));
        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapGet("/{id:guid}", Detail);
        group.MapPost("/{id:guid}/invite", Invite);
        group.MapPost("/{id:guid}/invitation", Respond);
    }

    private static async Task<IResult> List(ClaimsPrincipal user, BankDb db)
    {
        var id = Id(user);
        var result = await (from m in db.Memberships
                            join g in db.Groups on m.GroupId equals g.Id
                            join organizer in db.Users on g.OrganizerId equals organizer.Id
                            where m.UserId == id && m.Status != Declined
                            orderby g.Name, g.Id
                            select new { g.Id, g.Name, Organizer = organizer.Name, m.Status }).ToListAsync();
        return Results.Ok(result);
    }

    private static Task<IResult> Create(GroupName input, ClaimsPrincipal user, BankDb db)
    {
        var name = input.Name?.Trim();
        if (name is null or "" || name.Length > 100) return Task.FromResult(Error(400, "Vnesite ime skupine do 100 znakov."));
        return Run(db, async () =>
        {
            if (!await HasAccess(db, user, "CLIENT")) return Results.Unauthorized();
            var group = new BankGroup { Name = name, OrganizerId = Id(user) };
            db.Groups.Add(group);
            db.Memberships.Add(new GroupMembership { GroupId = group.Id, UserId = group.OrganizerId, Status = Member });
            return Results.Json(new { group.Id, group.Name }, statusCode: 201);
        });
    }

    private static async Task<IResult> Detail(Guid id, ClaimsPrincipal user, BankDb db)
    {
        var owner = Id(user);
        if (!await IsMember(db, id, owner)) return Results.NotFound();
        var group = (await db.Groups.FindAsync(id))!;
        var members = await (from m in db.Memberships
                             join u in db.Users on m.UserId equals u.Id
                             where m.GroupId == id && m.Status == Member
                             orderby u.Name, u.Id
                             select new { u.Id, u.Name, u.Active }).ToListAsync();
        return Results.Ok(new { group.Id, group.Name, organizer = group.OrganizerId == owner, members });
    }

    private static Task<IResult> Invite(Guid id, InvitationInput input, ClaimsPrincipal user, BankDb db) =>
        Run(db, async () =>
        {
            if (!await HasAccess(db, user, "CLIENT")) return Results.Unauthorized();
            var owner = Id(user);
            if (!await IsMember(db, id, owner)) return Results.NotFound();
            if (!await db.Groups.AnyAsync(g => g.Id == id && g.OrganizerId == owner)) return Results.Forbid();
            var email = input.Email?.Trim().ToLowerInvariant();
            var target = await db.Users.SingleOrDefaultAsync(u => u.Email == email && u.Active && u.Role == "CLIENT");
            if (target is null) return Error(400, "Aktivni komitent s tem naslovom ni na voljo.");
            var membership = await db.Memberships.FindAsync(id, target.Id);
            if (membership is null) db.Memberships.Add(new GroupMembership { GroupId = id, UserId = target.Id });
            else if (membership.Status == Declined) membership.Status = Invited;
            return Results.NoContent();
        });

    private static Task<IResult> Respond(Guid id, InvitationResponse input, ClaimsPrincipal user, BankDb db) =>
        Run(db, async () =>
        {
            if (!await HasAccess(db, user, "CLIENT")) return Results.Unauthorized();
            if (input.Accept is null) return Error(400, "Izberite sprejem ali zavrnitev.");
            var membership = await db.Memberships.FindAsync(id, Id(user));
            if (membership?.Status != Invited) return Results.NotFound();
            membership.Status = input.Accept.Value ? Member : Declined;
            return Results.NoContent();
        });

    public static Task<bool> IsMember(BankDb db, Guid group, Guid user) =>
        db.Memberships.AnyAsync(x => x.GroupId == group && x.UserId == user && x.Status == Member);
}

internal record GroupName(string? Name);
internal record InvitationInput(string? Email);
internal record InvitationResponse(bool? Accept);
