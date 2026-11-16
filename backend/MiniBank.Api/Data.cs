using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace MiniBank.Api;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "CLIENT";
    public bool Active { get; set; } = true;
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
}
public class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Number { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Balance { get; set; }
}
public class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceAccountId { get; set; }
    public Guid TargetAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = "";
    public string Status { get; set; } = "EXECUTED";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExecutedAt { get; set; }
    public Guid? RequestedById { get; set; }
    public Guid? RequestKey { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? ObligationId { get; set; }
}
public class BankDb(DbContextOptions<BankDb> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BankAccount> Accounts => Set<BankAccount>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<BankGroup> Groups => Set<BankGroup>();
    public DbSet<GroupMembership> Memberships => Set<GroupMembership>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseShare> Shares => Set<ExpenseShare>();
    public DbSet<Obligation> Obligations => Set<Obligation>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<AppUser>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<BankAccount>().HasIndex(x => x.Number).IsUnique();
        b.Entity<BankAccount>().Property(x => x.Balance).HasPrecision(15, 2);
        b.Entity<BankAccount>().ToTable(t => t.HasCheckConstraint("CK_Account_Balance", "\"Balance\" >= 0"));
        b.Entity<BankAccount>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transfer>().Property(x => x.Amount).HasPrecision(15, 2);
        b.Entity<Transfer>().ToTable(t => t.HasCheckConstraint("CK_Transfer_Amount", "\"Amount\" > 0"));
        b.Entity<Transfer>().HasOne<BankAccount>().WithMany().HasForeignKey(x => x.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transfer>().HasOne<BankAccount>().WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transfer>().HasIndex(x => new { x.SourceAccountId, x.ExecutedAt });
        b.Entity<Transfer>().HasIndex(x => new { x.TargetAccountId, x.ExecutedAt });
        b.Entity<Transfer>().HasIndex(x => new { x.RequestedById, x.RequestKey }).IsUnique();
        b.Entity<Transfer>().HasIndex(x => new { x.Status, x.CreatedAt });
        b.Entity<Transfer>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transfer>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transfer>().HasOne<Obligation>().WithMany().HasForeignKey(x => x.ObligationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transfer>().HasIndex(x => x.ObligationId).IsUnique()
            .HasFilter("\"ObligationId\" IS NOT NULL AND \"Status\" IN ('PENDING_APPROVAL', 'EXECUTED')");
        GroupModel.Configure(b);
    }
}
public static class DevelopmentSeed
{
    public static async Task Run(BankDb db, IConfiguration config)
    {
        if (await db.Users.AnyAsync()) return;
        var password = config["Seed:Password"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException("Seed:Password mora vsebovati vsaj 12 znakov.");
        var hasher = new PasswordHasher<AppUser>();
        foreach (var (name, role) in new[] { ("Ana", "CLIENT"), ("Bor", "CLIENT"), ("Cene", "CLIENT"), ("Usluzbenec", "EMPLOYEE"), ("Admin", "ADMIN") })
        {
            var user = new AppUser { Name = name, Email = $"{name.ToLowerInvariant()}@minibank.test", Role = role };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            if (role == "CLIENT") db.Accounts.Add(new BankAccount { OwnerId = user.Id, Label = "Osebni račun", Number = $"MB-{name.ToUpperInvariant()}", Balance = 200m });
        }
        await db.SaveChangesAsync();
    }
}
