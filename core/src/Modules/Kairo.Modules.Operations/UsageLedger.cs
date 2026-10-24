using System.Security.Cryptography;
using System.Text.Json;
using Kairo.Contracts.Operations;
using Kairo.Modules.Operations.Data;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Operations;

internal sealed class UsageLedger(OperationsDbContext db, KairoDatabase database, IConfiguration configuration, TimeProvider time) : IUsageLedger
{
    public async Task<AgentInvocation> OpenAsync(AgentUsageScope scope, string operation, CancellationToken ct)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var row = new Invocation { Id = Guid.NewGuid(), BrandId = scope.BrandId, CampaignId = scope.CampaignId, RunId = scope.RunId,
            Operation = "/" + operation.Trim('/'), TokenHash = UsagePolicy.Hash(token), ExpiresAt = time.GetUtcNow().AddMinutes(25) };
        db.Invocations.Add(row);
        await db.SaveChangesAsync(ct);
        return new AgentInvocation(row.Id, row.BrandId, token);
    }

    public async Task<ProviderCall> ReserveAsync(Guid invocationId, ReserveCall request, CancellationToken ct)
    {
        UsagePolicy.Validate(request);
        await database.BeginTransactionAsync(ct);
        try
        {
            var invocation = await db.Invocations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == invocationId && row.ExpiresAt > time.GetUtcNow(), ct)
                ?? throw new UsageFailure("invocation_unavailable", 403);
            var allowed = invocation.Operation switch
            {
                "/create/images" => request.Operation == "image",
                "/create/footage" => request.Operation is "text" or "video",
                "/create/video" => request.Operation is "text" or "speech",
                _ => request.Operation == "text",
            };
            if (!allowed) throw new UsageFailure("invalid_usage_operation", 400);
            await database.LockAsync($"usage:{invocation.BrandId:N}:{invocation.CampaignId ?? invocation.Id:N}", ct);
            var existing = await db.Calls.AsNoTracking().SingleOrDefaultAsync(row => row.InvocationId == invocationId && row.RequestId == request.RequestId, ct);
            var json = UsagePolicy.Json(request);
            if (existing is not null)
            {
                if (existing.Reservation != json) throw new UsageFailure("usage_conflict", 409);
                await database.CommitAsync(ct);
                return existing;
            }
            if (invocation.Closed) throw new UsageFailure("invocation_unavailable", 403);
            if (invocation.CampaignId is { } campaign)
                UsagePolicy.Enforce(await CapacityAsync(campaign, ct), request, UsagePolicy.Limits(configuration));
            var call = new ProviderCall { Id = Guid.NewGuid(), InvocationId = invocation.Id, CampaignId = invocation.CampaignId, RunId = invocation.RunId,
                RequestId = request.RequestId, Model = request.Model, Operation = request.Operation, Region = request.Region,
                Reservation = json, TextCalls = request.TextCalls, Images = request.Images, VideoClips = request.VideoClips,
                SpeechCharacters = request.SpeechCharacters, CreatedAt = time.GetUtcNow() };
            db.Calls.Add(call);
            await db.SaveChangesAsync(ct);
            await database.CommitAsync(ct);
            return call;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task ReportAsync(Guid invocationId, Guid requestId, ReportCall request, CancellationToken ct)
    {
        await database.BeginTransactionAsync(ct);
        try
        {
            await database.LockAsync($"usage-report:{invocationId:N}:{requestId:N}", ct);
            var call = await db.Calls.SingleOrDefaultAsync(row => row.InvocationId == invocationId && row.RequestId == requestId, ct)
                ?? throw new UsageFailure("usage_unavailable", 404);
            var reservation = JsonSerializer.Deserialize<ReserveCall>(call.Reservation, JsonSerializerOptions.Web)!;
            UsagePolicy.Validate(request, reservation);
            var report = UsagePolicy.Json(request);
            if (call.Report is not null && call.Report != report) throw new UsageFailure("usage_conflict", 409);
            if (call.Report is null)
            {
                call.Report = report;
                call.Outcome = request.Outcome;
                call.ResolvedModel = request.ResolvedModel;
                call.InputTokens = request.InputTokens;
                call.OutputTokens = request.OutputTokens;
                call.ThoughtTokens = request.ThoughtTokens;
                call.CachedTokens = request.CachedTokens;
                call.GeneratedImages = request.GeneratedImages;
                call.VideoSeconds = request.VideoSeconds;
                call.SpeechCharacters = request.SpeechCharacters;
                call.SpeechSeconds = request.SpeechSeconds;
                (call.EstimatedUsd, call.RateVersion) = UsageRates.Estimate(reservation, request, call.CreatedAt);
                call.ReportedAt = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
            await database.CommitAsync(ct);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<GenerationCapacity> CapacityAsync(Guid campaign, CancellationToken ct) => new(
        await db.Calls.Where(row => row.CampaignId == campaign).SumAsync(row => row.TextCalls, ct),
        await db.Calls.Where(row => row.CampaignId == campaign).SumAsync(row => row.Images, ct),
        await db.Calls.Where(row => row.CampaignId == campaign).SumAsync(row => row.VideoClips, ct));

    public async Task<CampaignUsage> ReadAsync(Guid campaignId, CancellationToken ct)
    {
        var calls = await db.Calls.AsNoTracking().Where(row => row.CampaignId == campaignId).ToListAsync(ct);
        var consumed = new GenerationCapacity(calls.Sum(row => row.TextCalls), calls.Sum(row => row.Images), calls.Sum(row => row.VideoClips));
        var limits = UsagePolicy.Limits(configuration);
        var unknown = calls.Count(row => row.EstimatedUsd is null);
        return new CampaignUsage(campaignId, calls.Count, consumed, limits,
            new GenerationCapacity(Math.Max(0, limits.TextCalls - consumed.TextCalls), Math.Max(0, limits.Images - consumed.Images), Math.Max(0, limits.VideoClips - consumed.VideoClips)),
            new UsageTotals(Total(calls.Where(row => row.Operation is "text" or "image").Select(row => row.InputTokens)), Total(calls.Where(row => row.Operation is "text" or "image").Select(row => row.OutputTokens)), Total(calls.Where(row => row.Operation is "text" or "image").Select(row => row.ThoughtTokens)),
                Total(calls.Where(row => row.Operation is "text" or "image").Select(row => row.CachedTokens)), ImageTotal(calls), DecimalTotal(calls.Where(row => row.Operation == "video").Select(row => row.VideoSeconds)),
                Total(calls.Where(row => row.Operation == "speech").Select(row => row.SpeechCharacters)), DecimalTotal(calls.Where(row => row.Operation == "speech").Select(row => row.SpeechSeconds))),
            calls.Sum(row => row.EstimatedUsd ?? 0), unknown == 0, unknown);
    }

    private static int? ImageTotal(List<ProviderCall> calls) => calls.Any(row => row.Operation == "image" && row.GeneratedImages is null)
        ? null : calls.Sum(row => row.GeneratedImages ?? 0);
    private static long? Total(IEnumerable<long?> values)
    {
        var counts = values.ToArray();
        return counts.Any(value => value is null) ? null : counts.Sum(value => value ?? 0);
    }
    private static decimal? DecimalTotal(IEnumerable<decimal?> values)
    {
        var counts = values.ToArray();
        return counts.Any(value => value is null) ? null : counts.Sum(value => value ?? 0);
    }
}
