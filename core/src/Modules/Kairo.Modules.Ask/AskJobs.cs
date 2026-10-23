using Kairo.Contracts.Ask;
using Kairo.Contracts.Live;
using Kairo.Modules.Ask.Data;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Ask;

internal sealed class ClaimedProposal
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
}

internal sealed class AskJobs(AskDbContext db, KairoDatabase database, IServiceScopeFactory scopes, TimeProvider time, ILogger<AskJobs> logger)
{
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var lease = Guid.NewGuid();
        var now = time.GetUtcNow();
        var until = now.AddMinutes(20);
        List<ClaimedProposal> claimed;
        await using (await database.EnterSystemScopeAsync(ct))
        {
            claimed = await db.Database.SqlQuery<ClaimedProposal>($"""
                UPDATE TOP (2) ask.Proposals WITH (READPAST, UPDLOCK, ROWLOCK)
                SET Status = N'generating', Lease = {lease}, DueAt = {until}, Attempts = Attempts + 1
                OUTPUT inserted.Id, inserted.BrandId
                WHERE Status IN (N'queued', N'generating') AND DueAt <= {now}
                """).ToListAsync(ct);
        }
        await Task.WhenAll(claimed.Select(async item =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                await services.GetRequiredService<KairoDatabase>().UseBrandAsync(item.BrandId, ct);
                await services.GetRequiredService<AskPreviewRunner>().RunAsync(item.Id, lease, ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogError(exception, "Ask preview {ProposalId} stopped; its lease permits recovery.", item.Id);
            }
        }));
        return claimed.Count;
    }
}

internal sealed class AskPreviewRunner(AskDbContext db, KairoDatabase database, CurrentMember member, IBrandAccess access,
    AskService ask, TimeProvider time, AgentUsageContext usage)
{
    public async Task RunAsync(Guid id, Guid lease, CancellationToken ct)
    {
        var row = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.Lease == lease && value.Status == "generating", ct);
        if (row is null) return;
        AskChange? preview = null;
        string? error = null;
        try
        {
            if (row.Attempts > 2) throw new AskResourceException("Preview generation failed twice. Ask for a new preview.");
            if (!await member.LoadAsync(row.BrandId, row.UserId, access, ct)) throw new AskResourceException("Workspace access was removed.", 403);
            await ask.PublishAsync("ask.proposal.changed", new { row.ConversationId, proposal = AskService.View(row) }, ct);
            var target = AskPolicy.Read<AskTarget>(row.Target);
            var resource = ask.Resource(target, true);
            AskJson.Unchanged(row.Revision, await resource.ReadAsync(target, ct));
            var campaignId = target.Kind is "campaign" or "brief" ? target.Id : target.Kind == "asset" ? target.CampaignId : null;
            using var usageScope = usage.Enter(new AgentUsageScope(row.BrandId, campaignId, row.Id));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(18));
            preview = await resource.PreviewAsync(target, AskPolicy.Read<System.Text.Json.JsonElement>(row.Changes), deadline.Token);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            error = exception is AgentCallException { Code: AgentCallException.GenerationLimitExceeded }
                ? AgentCallException.GenerationLimitExceeded
                : exception is AskResourceException known ? known.Message : "Kairo couldn't generate this preview. Ask for a new preview.";
            if (error.Length > 500) error = error[..500];
        }
        await database.BeginTransactionAsync(ct);
        try
        {
            await database.LockAsync($"ask-proposal:{id:N}", ct);
            var proposal = await db.Proposals.SingleOrDefaultAsync(value => value.Id == id && value.Lease == lease && value.Status == "generating", ct);
            if (proposal is not null)
            {
                proposal.Preview = preview is null ? null : AskPolicy.Write(preview);
                proposal.Error = error;
                proposal.Status = preview is null ? "failed" : "ready";
                proposal.ExpiresAt = time.GetUtcNow() + AskPolicy.Lifetime;
                proposal.Lease = null;
                await db.SaveChangesAsync(ct);
                if (member.UserId == row.UserId)
                    await ask.PublishAsync("ask.proposal.changed", new { row.ConversationId, proposal = AskService.View(proposal) }, ct);
            }
            await database.CommitAsync(ct);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

internal sealed class AskLoopSwitch
{
    public bool Enabled { get; private set; }
    public void Enable() => Enabled = true;
}

internal sealed class AskLoop(IServiceScopeFactory scopes, AskLoopSwitch enabled, IConfiguration configuration, ILogger<AskLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!enabled.Enabled || configuration["Kairo:Ask:PreviewLoop"] == "off") continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AskJobs>().RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Ask preview jobs will retry on the next sweep.");
            }
        }
    }
}
