using Kairo.Modules.Identity.Members;
using Kairo.Modules.Identity.Sessions;
using Kairo.Modules.Identity.Users;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Kairo.Modules.Identity.Data;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database), IDataProtectionKeyContext
{
    public const string SchemaName = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<DutyAssignment> DutyAssignments => Set<DutyAssignment>();

    public DbSet<BudgetLimit> BudgetLimits => Set<BudgetLimit>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasIndex(u => u.GoogleSubject).IsUnique();
            user.HasIndex(u => u.Email);
            user.Property(u => u.GoogleSubject).HasMaxLength(255);
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Name).HasMaxLength(200);
            user.Property(u => u.PictureUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("Sessions");
            session.HasKey(s => s.Key);
            session.Property(s => s.Key).HasMaxLength(64);
            session.HasIndex(s => s.UserId);
            session.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("Memberships");
            membership.HasKey(m => new { m.BrandId, m.UserId });
            membership.HasIndex(m => m.UserId);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DutyAssignment>(assignment =>
        {
            assignment.ToTable("ApprovalDuties");
            assignment.HasKey(a => new { a.BrandId, a.Duty, a.UserId });
            assignment.Property(a => a.Duty).HasConversion<string>().HasMaxLength(20);
            assignment.HasOne<Membership>().WithMany()
                .HasForeignKey(a => new { a.BrandId, a.UserId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BudgetLimit>(limit =>
        {
            limit.ToTable("BudgetLimits");
            limit.HasKey(l => l.BrandId);
            limit.Property(l => l.Amount).HasPrecision(19, 4);
            limit.Property(l => l.Currency).HasMaxLength(3).IsFixedLength();
        });
    }
}
