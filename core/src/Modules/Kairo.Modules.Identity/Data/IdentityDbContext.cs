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
    }
}
