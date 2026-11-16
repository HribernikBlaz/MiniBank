using Microsoft.EntityFrameworkCore;

namespace MiniBank.Api;

public class BankGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid OrganizerId { get; set; }
}

public class GroupMembership
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "INVITED";
}

public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid PayerId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? PreviousId { get; set; }
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "DRAFT";
    public List<ExpenseShare> Shares { get; set; } = [];
}

public class ExpenseShare
{
    public Guid ExpenseId { get; set; }
    public Guid UserId { get; set; }
    public decimal Value { get; set; }
    public string Decision { get; set; } = "PENDING";
    public string? Reason { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public class Obligation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseId { get; set; }
    public Guid DebtorId { get; set; }
    public Guid CreditorId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}

internal static class GroupModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<BankGroup>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OrganizerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<GroupMembership>().HasKey(x => new { x.GroupId, x.UserId });
        b.Entity<GroupMembership>().HasOne<BankGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<GroupMembership>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasOne<BankGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.PayerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasOne<BankAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasOne<Expense>().WithMany().HasForeignKey(x => x.PreviousId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasIndex(x => x.PreviousId).IsUnique();
        b.Entity<Expense>().Property(x => x.Total).HasPrecision(15, 2);
        b.Entity<ExpenseShare>().HasKey(x => new { x.ExpenseId, x.UserId });
        b.Entity<ExpenseShare>().HasOne<Expense>().WithMany(x => x.Shares).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseShare>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExpenseShare>().Property(x => x.Value).HasPrecision(15, 2);
        b.Entity<Obligation>().HasIndex(x => new { x.ExpenseId, x.DebtorId }).IsUnique();
        b.Entity<Obligation>().HasOne<Expense>().WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Obligation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.DebtorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Obligation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreditorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Obligation>().HasOne<BankAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Obligation>().Property(x => x.Amount).HasPrecision(15, 2);
    }
}
