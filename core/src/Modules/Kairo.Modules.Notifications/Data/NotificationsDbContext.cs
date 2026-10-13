namespace Kairo.Modules.Notifications.Data;

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "notifications";

    public DbSet<InAppNotification> Notifications => Set<InAppNotification>();

    public DbSet<EmailOptOut> EmailOptOuts => Set<EmailOptOut>();

    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();

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
            email.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            email.Property(e => e.LastError).HasMaxLength(500);
            email.HasOne(e => e.Notification).WithMany()
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
