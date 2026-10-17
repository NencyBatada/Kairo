using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Ask.Data;

internal sealed class Conversation : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class Message : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public Guid RequestId { get; set; }
    public string Question { get; set; } = "";
    public string Context { get; set; } = "{}";
    public string Status { get; set; } = "running";
    public string Segments { get; set; } = "[]";
    public string Evidence { get; set; } = "[]";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset TokenExpiresAt { get; set; }
    public int ToolCalls { get; set; }
    public string? Error { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

internal sealed class Proposal : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public string Target { get; set; } = "{}";
    public string Revision { get; set; } = "";
    public string Changes { get; set; } = "{}";
    public string? Preview { get; set; }
    public string Status { get; set; } = "queued";
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

internal sealed class AskDbContext(DbContextOptions<AskDbContext> options, KairoDatabase database) : ModuleDbContext(options, database)
{
    public const string SchemaName = "ask";
    protected override string Schema => SchemaName;
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Proposal> Proposals => Set<Proposal>();

    protected override void ConfigureModel(ModelBuilder builder)
    {
        builder.Entity<Conversation>(row =>
        {
            row.ToTable("Conversations");
            row.HasKey(value => value.Id);
            row.HasIndex(value => new { value.BrandId, value.UserId, value.UpdatedAt });
            row.Property(value => value.Title).HasMaxLength(120);
        });
        builder.Entity<Message>(row =>
        {
            row.ToTable("Messages");
            row.HasKey(value => value.Id);
            row.HasIndex(value => new { value.BrandId, value.ConversationId, value.RequestId }).IsUnique();
            row.HasIndex(value => new { value.BrandId, value.ConversationId, value.CreatedAt });
            row.Property(value => value.Question).HasMaxLength(4000);
            row.Property(value => value.Status).HasMaxLength(20);
            row.Property(value => value.TokenHash).HasMaxLength(64);
            row.Property(value => value.Error).HasMaxLength(500);
            row.Property(value => value.Model).HasMaxLength(100);
            row.Property(value => value.PromptVersion).HasMaxLength(100);
        });
        builder.Entity<Proposal>(row =>
        {
            row.ToTable("Proposals");
            row.HasKey(value => value.Id);
            row.HasIndex(value => new { value.BrandId, value.UserId, value.ConversationId });
            row.HasIndex(value => new { value.Status, value.DueAt });
            row.Property(value => value.Revision).HasMaxLength(64);
            row.Property(value => value.Status).HasMaxLength(20);
            row.Property(value => value.Error).HasMaxLength(500);
        });
    }
}

internal sealed class AskDbContextFactory : IDesignTimeDbContextFactory<AskDbContext>
{
    public AskDbContext CreateDbContext(string[] args) => KairoDbContextOptions.CreateForDesignTime<AskDbContext>(
        AskDbContext.SchemaName, (options, database) => new AskDbContext(options, database));
}
