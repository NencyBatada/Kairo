using System.Security.Cryptography;
using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Ask.Data;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Ask;

internal sealed record SendMessage(Guid RequestId, string Text, AskTarget Context);
internal sealed record AgentTurn(Guid BrandId, Guid MessageId, string Token, string Question, AskTarget Context, object[] History, DateTimeOffset Now);
internal sealed record AgentAnswer(string Model, string PromptVersion);
internal sealed record ConversationView(Guid Id, string Title, DateTimeOffset UpdatedAt);
internal sealed record MessageView(Guid Id, Guid RequestId, string Question, AskTarget Context, string Status, AnswerSegment[] Segments, Evidence[] Evidence, string? Error, DateTimeOffset CreatedAt, string? Model, string? PromptVersion);
internal sealed record PreviewFileView(string Name, string Url, string ContentType);
internal sealed record ProposalView(Guid Id, Guid MessageId, AskTarget Target, string Status, JsonElement? Before, JsonElement? After, string[] Warnings, PreviewFileView[] Files, string? Error, DateTimeOffset ExpiresAt, DateTimeOffset? AppliedAt);
internal sealed record ConversationDetail(ConversationView Conversation, MessageView[] Messages, ProposalView[] Proposals);

internal sealed class AskService(
    AskDbContext db, KairoDatabase database, CurrentMember member, IEnumerable<IAskResource> resources,
    IBrandDirectory brands, ILiveUpdates live, IAuditLog audit, AgentClient agent, TimeProvider time)
{
    public IAskResource Resource(AskTarget target, bool edit = false)
    {
        if (target is null) throw new AskResourceException("Supply the current item context.", 400);
        AskPolicy.Target(target);
        var resource = resources.SingleOrDefault(value => value.Kind == target.Kind)
            ?? throw new AskResourceException("This item isn't available to Ask Kairo.", 400);
        if (edit && (resource.Editors == BrandRoles.None || !member.Has(resource.Editors)))
            throw new AskResourceException("Your role cannot make this change.", 403);
        return resource;
    }

    public async Task<ConversationView> CreateAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var conversation = new Conversation { Id = Guid.NewGuid(), UserId = member.UserId, CreatedAt = now, UpdatedAt = now, Title = "Ask Kairo" };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return View(conversation);
    }

    public async Task<ConversationView[]> ListAsync(CancellationToken ct) =>
        [.. (await db.Conversations.AsNoTracking().Where(row => row.UserId == member.UserId).OrderByDescending(row => row.UpdatedAt).Take(50).ToListAsync(ct)).Select(View)];

    public async Task<ConversationDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var conversation = await OwnedAsync(id, ct);
        var messages = await db.Messages.AsNoTracking().Where(row => row.ConversationId == id && row.UserId == member.UserId)
            .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Take(100).ToListAsync(ct);
        var proposals = await db.Proposals.AsNoTracking().Where(row => row.ConversationId == id && row.UserId == member.UserId)
            .OrderByDescending(row => row.CreatedAt).Take(100).ToListAsync(ct);
        return new ConversationDetail(View(conversation), [.. messages.AsEnumerable().Reverse().Select(CurrentView)], [.. proposals.Select(View)]);
    }

    public async Task<MessageView> SendAsync(Guid id, SendMessage request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4000)
            throw new AskResourceException("Send a request ID and a question of 1 to 4000 characters.", 400);
        var resource = Resource(request.Context);
        if (await resource.ReadAsync(request.Context, ct) is null) throw new AskResourceException("The current item is unavailable.", 404);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        await database.BeginTransactionAsync(ct);
        Message message;
        try
        {
            await database.LockAsync($"ask-conversation:{id:N}", ct);
            var conversation = await OwnedAsync(id, ct);
            var previous = await db.Messages.AsNoTracking().SingleOrDefaultAsync(row => row.ConversationId == id && row.RequestId == request.RequestId && row.UserId == member.UserId, ct);
            if (previous is not null)
            {
                await database.CommitAsync(ct);
                return CurrentView(previous);
            }
            await db.Messages.Where(row => row.ConversationId == id && row.Status == "running" && row.TokenExpiresAt <= now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, "failed").SetProperty(row => row.Error, "This answer was interrupted. Ask again.").SetProperty(row => row.TokenHash, ""), ct);
            if (await db.Messages.AnyAsync(row => row.ConversationId == id && row.Status == "running", ct))
                throw new AskResourceException("Wait for the current answer to finish.");
            var last = await db.Messages.Where(row => row.ConversationId == id).MaxAsync(row => (DateTimeOffset?)row.CreatedAt, ct);
            message = new Message
            {
                Id = Guid.NewGuid(), ConversationId = id, UserId = member.UserId, RequestId = request.RequestId,
                Question = request.Text.Trim(), Context = AskPolicy.Write(request.Context), TokenHash = AskPolicy.Hash(token),
                TokenExpiresAt = now + AskPolicy.InvocationLifetime, CreatedAt = last >= now ? last.Value.AddTicks(1) : now,
            };
            conversation.Title = message.Question[..Math.Min(message.Question.Length, 120)];
            conversation.UpdatedAt = now;
            db.Messages.Add(message);
            await db.SaveChangesAsync(ct);
            await database.CommitAsync(ct);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }

        AgentAnswer? answer = null;
        var failed = false;
        try
        {
            var history = await db.Messages.AsNoTracking().Where(row => row.ConversationId == id && row.UserId == member.UserId && row.Id != message.Id && row.Status == "completed")
                .OrderByDescending(row => row.CreatedAt).Take(10).ToListAsync(ct);
            var turns = history.AsEnumerable().Reverse().SelectMany(row => new object[]
            {
                new { role = "user", text = row.Question },
                new { role = "model", text = HistoryText(row) },
            }).ToArray();
            answer = await agent.PostAsync<AgentTurn, AgentAnswer>("/ask/turn", new AgentTurn(member.BrandId, message.Id, token,
                message.Question, request.Context, turns, now), AskPolicy.InvocationLifetime, ct);
            if (answer.Model is not { Length: > 0 and <= 100 } || answer.PromptVersion is not { Length: > 0 and <= 100 })
                throw new AskResourceException("The agent returned invalid version information.");
        }
        catch (Exception exception) when (exception is AgentCallException or AskResourceException or OperationCanceledException)
        {
            failed = true;
        }
        await database.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await database.LockAsync($"ask-message:{message.Id:N}", CancellationToken.None);
            await db.Entry(message).ReloadAsync(CancellationToken.None);
            if (message.Status == "running")
            {
                failed |= AskPolicy.Read<AnswerSegment[]>(message.Segments).Length == 0;
                message.Status = failed ? "failed" : "completed";
                message.Error = failed ? "Kairo couldn't finish this answer. Your saved conversation is available; ask again." : null;
                message.Model = failed ? null : answer?.Model;
                message.PromptVersion = failed ? null : answer?.PromptVersion;
                message.TokenHash = "";
                message.FinishedAt = time.GetUtcNow();
                await db.SaveChangesAsync(CancellationToken.None);
                await PublishAsync("ask.message." + message.Status, new { conversationId = id, message = View(message) }, CancellationToken.None);
            }
            await database.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
        return CurrentView(message);
    }

    public async Task<object> ToolAsync(Guid messageId, ToolRequest request, CancellationToken ct)
    {
        await database.LockAsync($"ask-message:{messageId:N}", ct);
        var message = await db.Messages.SingleOrDefaultAsync(row => row.Id == messageId && row.UserId == member.UserId, ct)
            ?? throw new AskResourceException("The invocation is unavailable.", 404);
        if (message.Status != "running" || message.TokenExpiresAt <= time.GetUtcNow()) throw new AskResourceException("The invocation expired.", 403);
        if (++message.ToolCalls > AskPolicy.MaxTools) throw new AskResourceException("The tool limit was reached.", 429);
        var evidence = AskPolicy.Read<List<Evidence>>(message.Evidence);
        object result;
        if (request.Operation == "answer")
        {
            var segments = AskPolicy.Read<List<AnswerSegment>>(message.Segments);
            if (segments.Count >= 12) throw new AskResourceException("The answer is too long.", 400);
            var segment = AskPolicy.Segment(request.Text, request.EvidenceIds, evidence);
            segments.Add(segment);
            message.Segments = AskPolicy.Write(segments);
            result = new { accepted = true };
            await PublishAsync("ask.answer.segment", new { message.ConversationId, messageId, index = segments.Count - 1, segment }, ct);
        }
        else if (request.Operation is "read" or "propose" && request.Target is { } target)
        {
            var resource = Resource(target, request.Operation == "propose");
            var document = await resource.ReadAsync(target, ct) ?? throw new AskResourceException("This item is unavailable.", 404);
            if (document.Data.GetRawText().Length > 100000) throw new AskResourceException("This item is too large. Ask about a smaller item.", 400);
            if (request.Operation == "read")
            {
                var item = evidence.FirstOrDefault(row => row.Target == target);
                if (item is null)
                {
                    var brand = (await brands.GetAsync([member.BrandId], ct)).Single();
                    item = new Evidence($"e{evidence.Count + 1}", target, document.Title, $"/{Uri.EscapeDataString(brand.Handle)}/{document.Path.TrimStart('/')}", document.Data, AskPolicy.Sources(document.Data));
                    evidence.Add(item);
                    message.Evidence = AskPolicy.Write(evidence);
                }
                result = item;
            }
            else
            {
                if (request.Changes is not { ValueKind: JsonValueKind.Object } changes || changes.GetRawText().Length > 20000)
                    throw new AskResourceException("Supply supported change fields.", 400);
                var existing = await db.Proposals.Where(row => row.MessageId == messageId).ToListAsync(ct);
                if (existing.Any(row => row.Target == AskPolicy.Write(target) && row.Changes == changes.GetRawText()))
                    result = View(existing.First(row => row.Target == AskPolicy.Write(target) && row.Changes == changes.GetRawText()));
                else
                {
                    if (existing.Count >= 3) throw new AskResourceException("Preview at most three changes per question.", 429);
                    var now = time.GetUtcNow();
                    var proposal = new Proposal
                    {
                        Id = Guid.NewGuid(), UserId = member.UserId, ConversationId = message.ConversationId, MessageId = messageId,
                        Target = AskPolicy.Write(target), Revision = document.Revision, Changes = changes.GetRawText(),
                        CreatedAt = now, DueAt = now, ExpiresAt = now + AskPolicy.Lifetime,
                    };
                    db.Proposals.Add(proposal);
                    result = View(proposal);
                    await audit.RecordAsync(new AuditRecord("ask.proposal_created", "ask_proposal", proposal.Id.ToString(), After: new { target, conversationId = message.ConversationId }), ct);
                    await PublishAsync("ask.proposal.changed", new { conversationId = message.ConversationId, proposal = View(proposal) }, ct);
                }
            }
        }
        else throw new AskResourceException("Only read, propose and answer tools are available.", 400);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<ProposalView> ProposalAsync(Guid conversationId, Guid proposalId, CancellationToken ct) =>
        View(await ProposalRowAsync(conversationId, proposalId, ct));

    public async Task<AskPreviewFile> PreviewFileAsync(Guid conversationId, Guid proposalId, string name, CancellationToken ct)
    {
        var row = await ProposalRowAsync(conversationId, proposalId, ct);
        if (row.Status is not ("ready" or "applied") || row.ExpiresAt <= time.GetUtcNow() || row.Preview is null)
            throw new AskResourceException("This preview is unavailable.", 404);
        return AskPolicy.Read<AskChange>(row.Preview).Files?.SingleOrDefault(file => file.Name == name)
            ?? throw new AskResourceException("This preview file is unavailable.", 404);
    }

    public async Task<ProposalView> DecideAsync(Guid conversationId, Guid proposalId, bool confirm, CancellationToken ct)
    {
        await database.LockAsync($"ask-proposal:{proposalId:N}", ct);
        var proposal = await ProposalRowAsync(conversationId, proposalId, ct);
        var target = AskPolicy.Read<AskTarget>(proposal.Target);
        if (proposal.Status == "applied" && confirm) return View(proposal);
        if (!confirm)
        {
            if (proposal.Status == "applied") throw new AskResourceException("This change was already applied.");
            proposal.Status = "rejected";
            proposal.Lease = null;
        }
        else
        {
            var resource = Resource(target, true);
            if (proposal.Status != "ready" || proposal.Preview is null || proposal.ExpiresAt <= time.GetUtcNow())
                throw new AskResourceException("This preview isn't ready or has expired. Ask for a new preview.");
            var preview = AskPolicy.Read<AskChange>(proposal.Preview);
            await resource.ApplyAsync(target, proposal.Revision, preview.Payload, ct);
            proposal.Status = "applied";
            proposal.AppliedAt = time.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditRecord(confirm ? "ask.proposal_applied" : "ask.proposal_rejected", "ask_proposal", proposal.Id.ToString(), After: new { target, proposal.ConversationId }), ct);
        await PublishAsync("ask.proposal.changed", new { conversationId, proposal = View(proposal) }, ct);
        return View(proposal);
    }

    public Task PublishAsync(string kind, object body, CancellationToken ct) => live.PublishAsync(new LiveEvent(kind, body, LiveAudience.Users(member.UserId)), ct);

    private async Task<Conversation> OwnedAsync(Guid id, CancellationToken ct) =>
        await db.Conversations.SingleOrDefaultAsync(row => row.Id == id && row.UserId == member.UserId, ct)
        ?? throw new AskResourceException("This conversation is unavailable.", 404);

    private async Task<Proposal> ProposalRowAsync(Guid conversationId, Guid proposalId, CancellationToken ct)
    {
        await OwnedAsync(conversationId, ct);
        return await db.Proposals.SingleOrDefaultAsync(row => row.Id == proposalId && row.ConversationId == conversationId && row.UserId == member.UserId, ct)
            ?? throw new AskResourceException("This proposal is unavailable.", 404);
    }

    internal static ConversationView View(Conversation row) => new(row.Id, row.Title, row.UpdatedAt);
    private MessageView CurrentView(Message row)
    {
        var view = View(row);
        return row.Status == "running" && row.TokenExpiresAt <= time.GetUtcNow()
            ? view with { Status = "failed", Error = "This answer was interrupted. Ask again." }
            : view;
    }
    private static string HistoryText(Message row)
    {
        var text = string.Join("\n", AskPolicy.Read<AnswerSegment[]>(row.Segments).Select(segment => segment.Text));
        return text[..Math.Min(12000, text.Length)];
    }
    internal static MessageView View(Message row) => new(row.Id, row.RequestId, row.Question, AskPolicy.Read<AskTarget>(row.Context),
        row.Status, AskPolicy.Read<AnswerSegment[]>(row.Segments), AskPolicy.Read<Evidence[]>(row.Evidence), row.Error, row.CreatedAt, row.Model, row.PromptVersion);
    internal static ProposalView View(Proposal row)
    {
        var change = row.Preview is null ? null : AskPolicy.Read<AskChange>(row.Preview);
        return new(row.Id, row.MessageId, AskPolicy.Read<AskTarget>(row.Target), row.Status, change?.Before, change?.After, change?.Warnings ?? [],
            [.. (change?.Files ?? []).Select(file => new PreviewFileView(file.Name, $"/api/brands/{row.BrandId}/ask/conversations/{row.ConversationId}/proposals/{row.Id}/files/{Uri.EscapeDataString(file.Name)}", file.ContentType))],
            row.Error, row.ExpiresAt, row.AppliedAt);
    }
}
