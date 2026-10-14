namespace Kairo.Modules.Notifications.Data;

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "notifications";

    public DbSet<InAppNotification> Notifications => Set<InAppNotification>();

    public DbSet<EmailOptOut> EmailOptOuts => Set<EmailOptOut>();

    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();

    public DbSet<HeldNotification> HeldNotifications => Set<HeldNotification>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InAppNotification>(notification =>
        {
            notification.ToTable("Notifications");
            notification.HasIndex(n => new { n.BrandId, n.UserId, n.Id }).IsDescending(false, false, true);
            notification.HasIndex(n => new { n.BrandId, n.UserId })
                .HasFilter("[ReadAt] IS NULL")
                .HasDatabaseName("IX_Notifications_Unread");
            notification.Property(n => n.Kind).HasMaxLength(64);
            notification.Property(n => n.Title).HasMaxLength(200);
            notification.Property(n => n.Body).HasMaxLength(1000);
            notification.Property(n => n.Link).HasMaxLength(400);
            notification.Property(n => n.Subject).HasMaxLength(200);
            notification.HasIndex(n => new { n.BrandId, n.Subject })
                .HasFilter("[Subject] IS NOT NULL AND [ResolvedAt] IS NULL")
                .HasDatabaseName("IX_Notifications_Unresolved");
            notification.HasIndex(n => n.RemindAt)
                .HasFilter("[RemindAt] IS NOT NULL")
                .HasDatabaseName("IX_Notifications_Reminders");
            notification.HasIndex(n => new { n.AwaitsDigest, n.BrandId })
                .HasFilter("[AwaitsDigest] = 1")
                .HasDatabaseName("IX_Notifications_AwaitingDigest");
            notification.HasIndex(n => n.DigestEmailId)
                .HasFilter("[DigestEmailId] IS NOT NULL");
        });

        modelBuilder.Entity<HeldNotification>(held =>
        {
            held.ToTable("HeldNotifications");
            held.HasIndex(h => new { h.BrandId, h.CreatedAt });
            held.HasIndex(h => h.CreatedAt);
            held.Property(h => h.Kind).HasMaxLength(64);
            held.Property(h => h.Title).HasMaxLength(200);
            held.Property(h => h.Body).HasMaxLength(1000);
            held.Property(h => h.Link).HasMaxLength(400);
        });

        modelBuilder.Entity<EmailOptOut>(optOut =>
        {
            optOut.ToTable("EmailOptOuts");
            optOut.HasKey(o => new { o.BrandId, o.UserId, o.Kind });
            optOut.Property(o => o.Kind).HasMaxLength(64);
        });

        modelBuilder.Entity<OutboundEmail>(email =>
        {
            email.ToTable("OutboundEmails");
            email.HasIndex(e => new { e.Status, e.NextAttemptAt });
            email.HasIndex(e => new { e.BrandId, e.Type, e.UserId, e.CreatedAt });
            email.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            email.Property(e => e.Address).HasMaxLength(320);
            email.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            email.Property(e => e.LastError).HasMaxLength(500);
            email.HasOne(e => e.Notification).WithMany()
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
