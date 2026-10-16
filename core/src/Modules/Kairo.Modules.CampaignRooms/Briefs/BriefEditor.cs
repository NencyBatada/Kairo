using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.BrandMemory;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.ProofPlans;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal enum BriefError
{
    None,
    NotFound,
    Stale,
    NotAllowed,
    Invalid,
    AgentFailed,
}

internal sealed record BriefResult<T>(T? Value, BriefError Error, string? Message = null, Dictionary<string, string[]>? Errors = null)
    where T : class
{
    public static BriefResult<T> Done(T value) => new(value, BriefError.None);

    public static BriefResult<T> Failed(BriefError error, string? message = null) => new(null, error, message);

    public static BriefResult<T> Invalid(Dictionary<string, string[]> errors) => new(null, BriefError.Invalid, null, errors);
}

internal sealed class BriefEditor(
    CampaignRoomsDbContext db,
    RoomWorkflow workflow,
    CampaignReader reader,
    BriefDesk desk,
    StageQueue queue,
    BriefAgent agent,
    IBrandKit brandKit,
    IBrandMemory brandMemory,
    IAuditLog audit,
    TimeProvider time,
    ILogger<BriefEditor> logger)
{
    public const int HistoryLimit = 50;
    public const int MaxOptionRegions = 60;

    public static readonly TimeSpan ProposalLife = TimeSpan.FromMinutes(30);

    public async Task<BriefDetail?> DetailAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == roomId, cancellationToken);
        return room is null ? null : await DetailAsync(room, cancellationToken);
    }

    public async Task<BriefDetail> DetailAsync(Room room, CancellationToken cancellationToken)
    {
        var briefs = await db.Briefs.AsNoTracking()
            .Where(brief => brief.RoomId == room.Id)
            .OrderByDescending(brief => brief.Version)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);
        var latest = briefs.FirstOrDefault();
        var content = latest?.Content.Normalized();
        var context = await desk.ReadAsync(room, content, true, cancellationToken);
        var people = await reader.MembersAsync(cancellationToken);
        var memory = briefs.FirstOrDefault(brief => brief.Origin == BriefOrigins.Drafted) is { } drafted
            ? await brandMemory.UsedByAsync(MemoryUsedIn.Brief, drafted.Id, cancellationToken)
            : [];
        return new BriefDetail(
            room.Id,
            room.Version,
            latest is not null && Blocker(room) is null,
            latest is null ? null : BriefViews.View(latest, content!, context),
            content is null ? [] : BriefViews.Warnings(BriefChecks.Run(content, context.Situation())),
            BriefViews.Limit(context.Limit),
            BriefViews.Options(context),
            [.. briefs.Select(brief => new BriefVersionView(
                brief.Version,
                brief.Origin,
                brief.CreatedBy is { } by ? CampaignReader.Person(by, people) : null,
                brief.Instruction,
                brief.CreatedAt))],
            [.. memory.Select(note => new BriefMemoryView(note.Id, note.Kind, note.Outcome, note.Title, note.Removed))]);
    }

    public async Task<BriefResult<BriefDetail>> EditAsync(Guid roomId, BriefEditRequest request, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, request.Version!.Value, request.BriefVersion!.Value, true, cancellationToken) is not { } opened)
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotFound);
        }

        if (opened.Error is { } error)
        {
            return BriefResult<BriefDetail>.Failed(error.Error, error.Message);
        }

        var (room, latest) = (opened.Room, opened.Brief!);
        var current = latest.Content.Normalized();
        var context = await desk.ReadAsync(room, current, false, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        var edited = Apply(current, request, context, errors);
        if (errors.Count > 0)
        {
            return BriefResult<BriefDetail>.Invalid(errors);
        }

        var fields = Changed(current, edited);
        if (fields.Count == 0)
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotAllowed, "Nothing in the brief changed.");
        }

        var name = CampaignReader.Person(member.UserId, await reader.MembersAsync(cancellationToken)).Name;
        foreach (var field in fields.Where(field => field != BriefFields.Title))
        {
            edited.SetReason(field, $"Changed by {name}.");
        }

        var brief = await SaveAsync(room, latest, edited, fields, BriefOrigins.Edit, member.UserId, null, null, null, cancellationToken);
        await FinishAsync(room, new AuditRecord("brief.edited", RoomWorkflow.AuditTarget, room.Id.ToString(), new { briefVersion = latest.Version }, new { briefVersion = brief.Version, fields }), cancellationToken);
        return BriefResult<BriefDetail>.Done(await DetailAsync(room, cancellationToken));
    }

    public async Task<BriefResult<BriefProposalView>> ProposeAsync(Guid roomId, BriefInstructionRequestBody request, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, request.Version!.Value, request.BriefVersion!.Value, false, cancellationToken) is not { } opened)
        {
            return BriefResult<BriefProposalView>.Failed(BriefError.NotFound);
        }

        if (opened.Error is { } error)
        {
            return BriefResult<BriefProposalView>.Failed(error.Error, error.Message);
        }

        var (room, latest) = (opened.Room, opened.Brief!);
        var current = latest.Content.Normalized();
        var context = await desk.ReadAsync(room, current, true, cancellationToken);
        var keyed = context.Products.Where(product => product.InStock).Take(BriefLimits.Products)
            .Select((product, index) => (Key: $"p{index + 1}", Product: product))
            .ToList();
        if (keyed.Count == 0)
        {
            return BriefResult<BriefProposalView>.Failed(BriefError.NotAllowed, "None of this campaign's products are in stock.");
        }

        var instruction = request.Instruction!.Trim();
        var currency = current.Budget?.Currency is { Length: 3 } held ? held : context.Brand.Currency;
        var kit = await brandKit.GetApprovedAsync(cancellationToken);
        BriefInstructionAnswer answer;
        try
        {
            answer = await agent.InstructAsync(
                new BriefInstructionRequest(
                    context.Brand.Name,
                    BriefDesk.Iso(context.Today),
                    instruction,
                    new CurrentBriefData(
                        Cut(current.Title, 200),
                        Cut(current.Objective, 500),
                        current.Kpi ?? "",
                        [.. (current.CampaignTypes ?? []).Take(3)],
                        current.Budget?.Amount,
                        currency,
                        current.StartDate is { } start ? BriefDesk.Iso(start) : null,
                        current.EndDate is { } end ? BriefDesk.Iso(end) : null,
                        [.. (current.Regions ?? []).Take(BriefLimits.Regions).Select(region => Cut(region, 100))],
                        [.. (current.Audiences ?? []).Where(text => text.Length > 0).Take(BriefLimits.Audiences).Select(text => Cut(text, 200))],
                        [.. (current.KeyMessages ?? []).Where(text => text.Length > 0).Take(BriefLimits.KeyMessages).Select(text => Cut(text, 200))],
                        [.. (current.ProductIds ?? []).Select(id => keyed.FirstOrDefault(entry => entry.Product.Id == id).Key).OfType<string>()],
                        [.. (current.Languages ?? []).Take(12)]),
                    new InstructionOptions(
                        [.. context.Regions.Take(MaxOptionRegions).Select(region => Cut(region.Name, 100))],
                        [.. keyed.Select(entry => new SmartProduct(entry.Key, Cut(entry.Product.Title, 150), entry.Product.Price, entry.Product.Currency))],
                        [.. BriefChoices.Languages.Select(language => language.Value)]),
                    kit?.Content),
                cancellationToken);
        }
        catch (AgentCallException exception)
        {
            logger.LogWarning(exception, "The Plan agent couldn't follow a brief instruction for room {RoomId}.", room.Id);
            return BriefResult<BriefProposalView>.Failed(BriefError.AgentFailed, "Kairo couldn't work out that change. Try again, or edit the fields directly.");
        }

        var changes = answer.Changes;
        var proposed = current.Copy();
        if (changes is not null)
        {
            var edit = new BriefEditRequest(
                null,
                null,
                changes.Title,
                changes.Objective,
                changes.Kpi,
                Strings(changes.CampaignTypes),
                changes.Budget,
                BriefDesk.ParseDay(changes.StartDate),
                BriefDesk.ParseDay(changes.EndDate),
                Strings(changes.Regions),
                Strings(changes.Audiences),
                Strings(changes.KeyMessages),
                changes.ProductIds is null ? null : [.. changes.ProductIds.OfType<string>().Select(key => keyed.FirstOrDefault(entry => entry.Key == key.Trim()).Product?.Id).OfType<string>()],
                null,
                Strings(changes.Languages));
            proposed = ApplyLeniently(current, edit, context);
        }

        var fields = Changed(current, proposed);
        foreach (var field in fields.Where(field => field != BriefFields.Title))
        {
            var reason = BriefWork.Text(changes?.Reasons?.For(field), BriefLimits.Reason) ?? $"Changed to follow: \"{instruction}\"";
            proposed.SetReason(field, reason);
        }

        var now = time.GetUtcNow();
        var proposal = new BriefProposal
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            BriefVersion = latest.Version,
            Instruction = instruction,
            Content = proposed,
            Fields = fields,
            Summary = BriefWork.Text(changes?.Summary, 500) ?? (fields.Count == 0 ? "Kairo couldn't find anything to change for that instruction." : ""),
            Model = answer.Model,
            PromptVersion = answer.PromptVersion,
            CreatedBy = member.UserId,
            CreatedAt = now,
            ExpiresAt = now + ProposalLife,
        };
        db.BriefProposals.Add(proposal);
        await db.SaveChangesAsync(cancellationToken);
        var preview = new Brief { Version = latest.Version, Origin = BriefOrigins.Instruction, Model = answer.Model, PromptVersion = answer.PromptVersion, CreatedAt = now };
        return BriefResult<BriefProposalView>.Done(new BriefProposalView(
            proposal.Id,
            proposal.BriefVersion,
            proposal.Instruction,
            proposal.Summary,
            [.. fields.Select(field => new BriefChangeView(
                field,
                BriefFields.Label(field),
                BriefViews.Display(field, current, context),
                BriefViews.Display(field, proposed, context),
                proposed.ReasonFor(field)?.Reason ?? ""))],
            BriefViews.View(preview, proposed, context),
            BriefViews.Warnings(BriefChecks.Run(proposed, context.Situation())),
            proposal.ExpiresAt));
    }

    public async Task<BriefResult<BriefDetail>> ApplyAsync(Guid roomId, Guid proposalId, int version, CurrentMember member, CancellationToken cancellationToken)
    {
        var proposal = await db.BriefProposals.SingleOrDefaultAsync(row => row.Id == proposalId && row.RoomId == roomId, cancellationToken);
        if (proposal is null)
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotFound);
        }

        if (await OpenAsync(roomId, version, proposal.BriefVersion, true, cancellationToken) is not { } opened)
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotFound);
        }

        if (opened.Error is { } error)
        {
            return BriefResult<BriefDetail>.Failed(
                error.Error,
                error.Error == BriefError.Stale && opened.Brief is not null ? "The brief changed since this preview. Type the instruction again." : error.Message);
        }

        if (proposal.AppliedAt is not null)
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotAllowed, "These changes were already applied.");
        }

        if (proposal.ExpiresAt <= time.GetUtcNow())
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotAllowed, "This preview has expired. Type the instruction again.");
        }

        if (proposal.Fields.Count == 0)
        {
            return BriefResult<BriefDetail>.Failed(BriefError.NotAllowed, "This instruction didn't change anything.");
        }

        var (room, latest) = (opened.Room, opened.Brief!);
        proposal.AppliedAt = time.GetUtcNow();
        var brief = await SaveAsync(room, latest, proposal.Content.Copy(), proposal.Fields, BriefOrigins.Instruction, member.UserId, proposal.Instruction, proposal.Model, proposal.PromptVersion, cancellationToken);
        await FinishAsync(
            room,
            new AuditRecord(
                "brief.instruction_applied",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                new { briefVersion = latest.Version },
                new { briefVersion = brief.Version, fields = proposal.Fields, instruction = proposal.Instruction }),
            cancellationToken);
        return BriefResult<BriefDetail>.Done(await DetailAsync(room, cancellationToken));
    }

    public static bool RedesignsTest(IEnumerable<string> fields) =>
        fields.Any(field => field is BriefFields.Regions or BriefFields.Dates);

    public static string? Blocker(Room room)
    {
        if (room.State != RoomStates.BriefDraft)
        {
            return "The brief can only change while the campaign is in brief draft.";
        }

        if (room.PendingGate is not null)
        {
            return "Approval was requested, so the brief can't change until it's approved or sent back.";
        }

        return room.RunStatus is StageRunStatuses.Queued or StageRunStatuses.Running
            ? "Wait for the Plan agent to finish before changing the brief."
            : null;
    }

    public static BriefContent Apply(BriefContent current, BriefEditRequest request, BriefDeskContext context, Dictionary<string, string[]> errors)
    {
        var edited = current.Copy();
        if (request.Title is { } title)
        {
            if (Required(title, BriefLimits.Title) is { } text)
            {
                edited.Title = text;
            }
            else
            {
                errors["title"] = [$"Enter a title of up to {BriefLimits.Title} characters."];
            }
        }

        if (request.Objective is { } objective)
        {
            if (Required(objective, BriefLimits.Objective) is { } text)
            {
                edited.Objective = text;
            }
            else
            {
                errors["objective"] = [$"Enter an objective of up to {BriefLimits.Objective} characters."];
            }
        }

        if (request.Kpi is { } kpi)
        {
            if (BriefChoices.IsKpi(kpi))
            {
                edited.Kpi = kpi;
            }
            else
            {
                errors["kpi"] = [$"Use one of: {string.Join(", ", BriefChoices.Kpis.Select(choice => choice.Value))}."];
            }
        }

        if (request.CampaignTypes is { } types)
        {
            if (types.Length is > 0 and <= 3 && types.All(BriefChoices.IsCampaignType) && types.Distinct().Count() == types.Length)
            {
                edited.CampaignTypes = [.. types];
            }
            else
            {
                errors["campaignTypes"] = [$"Choose one or more of: {string.Join(", ", BriefChoices.CampaignTypes.Select(choice => choice.Value))}."];
            }
        }

        if (request.Budget is { } budget)
        {
            if (budget is > 0 and <= BriefLimits.MaxBudget)
            {
                edited.Budget = new BriefBudget { Amount = Math.Round(budget, 2), Currency = current.Budget?.Currency is { Length: > 0 } held ? held : context.Brand.Currency };
            }
            else
            {
                errors["budget"] = ["Enter a budget above zero."];
            }
        }

        if (request.StartDate is not null || request.EndDate is not null)
        {
            var start = request.StartDate ?? current.StartDate;
            var end = request.EndDate ?? current.EndDate;
            if (request.StartDate is { } day && day < context.Today)
            {
                errors["startDate"] = ["Pick today or a later day."];
            }
            else if (start is null || end is null)
            {
                errors["endDate"] = ["Set both the start and end dates."];
            }
            else if (end < start)
            {
                errors["endDate"] = ["Pick an end date on or after the start date."];
            }
            else
            {
                edited.StartDate = start;
                edited.EndDate = end;
            }
        }

        if (request.Regions is { } regions)
        {
            var known = regions.Select(region => context.Region(region ?? "")).ToList();
            if (regions.Length is > 0 and <= BriefLimits.Regions && known.All(region => region is not null))
            {
                edited.Regions = [.. known.OfType<string>().Distinct()];
            }
            else
            {
                errors["regions"] = [$"Choose 1 to {BriefLimits.Regions} regions from the list."];
            }
        }

        if (request.Audiences is { } audiences)
        {
            if (List(audiences, BriefLimits.Audience, BriefLimits.Audiences) is { } list)
            {
                edited.Audiences = list;
            }
            else
            {
                errors["audiences"] = [$"Describe 1 to {BriefLimits.Audiences} audiences of up to {BriefLimits.Audience} characters each."];
            }
        }

        if (request.KeyMessages is { } messages)
        {
            if (List(messages, BriefLimits.KeyMessage, BriefLimits.KeyMessages) is { } list)
            {
                edited.KeyMessages = list;
            }
            else
            {
                errors["keyMessages"] = [$"Write 1 to {BriefLimits.KeyMessages} key messages of up to {BriefLimits.KeyMessage} characters each."];
            }
        }

        if (request.ProductIds is { } products)
        {
            var chosen = products.Select(id => context.Product(id ?? "")).ToList();
            if (products.Length is > 0 and <= BriefLimits.Products && chosen.All(product => product is { InStock: true }))
            {
                edited.ProductIds = [.. chosen.Select(product => product!.Id).Distinct()];
                if (request.LandingPages is null)
                {
                    edited.LandingPages = BriefWork.LandingPages(chosen.OfType<BriefProductOption>());
                }
            }
            else
            {
                errors["productIds"] = [$"Choose 1 to {BriefLimits.Products} in-stock products from the list."];
            }
        }

        if (request.LandingPages is { } pages)
        {
            var trimmed = pages.Select(page => page?.Trim() ?? "").ToList();
            if (trimmed.Count <= BriefLimits.LandingPages && trimmed.All(BriefLimits.IsLink))
            {
                edited.LandingPages = [.. trimmed.Distinct(StringComparer.Ordinal)];
            }
            else
            {
                errors["landingPages"] = [$"Use up to {BriefLimits.LandingPages} web addresses starting with https://."];
            }
        }

        if (request.Languages is { } languages)
        {
            if (languages.Length is > 0 and <= BriefLimits.Languages && languages.All(language => BriefChoices.IsLanguage(language ?? "")))
            {
                edited.Languages = [.. languages.Distinct()];
            }
            else
            {
                errors["languages"] = [$"Choose 1 to {BriefLimits.Languages} languages from the list."];
            }
        }

        return edited;
    }

    public static BriefContent ApplyLeniently(BriefContent current, BriefEditRequest request, BriefDeskContext context)
    {
        var edited = current;
        foreach (var single in Split(request))
        {
            var errors = new Dictionary<string, string[]>();
            var next = Apply(edited, single, context, errors);
            if (errors.Count == 0)
            {
                edited = next;
            }
        }

        return edited == current ? current.Copy() : edited;
    }

    public static List<string> Changed(BriefContent before, BriefContent after)
    {
        var fields = new List<string>();
        void Check(string field, bool same)
        {
            if (!same)
            {
                fields.Add(field);
            }
        }

        Check(BriefFields.Title, before.Title == after.Title);
        Check(BriefFields.Objective, before.Objective == after.Objective);
        Check(BriefFields.Kpi, before.Kpi == after.Kpi);
        Check(BriefFields.CampaignTypes, Same(before.CampaignTypes, after.CampaignTypes));
        Check(BriefFields.Budget, before.Budget?.Amount == after.Budget?.Amount && before.Budget?.Currency == after.Budget?.Currency);
        Check(BriefFields.Dates, before.StartDate == after.StartDate && before.EndDate == after.EndDate);
        Check(BriefFields.Regions, Same(before.Regions, after.Regions));
        Check(BriefFields.Audiences, Same(before.Audiences, after.Audiences));
        Check(BriefFields.KeyMessages, Same(before.KeyMessages, after.KeyMessages));
        Check(BriefFields.Products, Same(before.ProductIds, after.ProductIds));
        Check(BriefFields.LandingPages, Same(before.LandingPages, after.LandingPages));
        Check(BriefFields.Languages, Same(before.Languages, after.Languages));
        return fields;
    }

    private static IEnumerable<BriefEditRequest> Split(BriefEditRequest request)
    {
        var empty = new BriefEditRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        BriefEditRequest?[] singles =
        [
            request.Title is null ? null : empty with { Title = request.Title },
            request.Objective is null ? null : empty with { Objective = request.Objective },
            request.Kpi is null ? null : empty with { Kpi = request.Kpi },
            request.CampaignTypes is null ? null : empty with { CampaignTypes = request.CampaignTypes },
            request.Budget is null ? null : empty with { Budget = request.Budget },
            request.StartDate is null && request.EndDate is null ? null : empty with { StartDate = request.StartDate, EndDate = request.EndDate },
            request.Regions is null ? null : empty with { Regions = request.Regions },
            request.Audiences is null ? null : empty with { Audiences = request.Audiences },
            request.KeyMessages is null ? null : empty with { KeyMessages = request.KeyMessages },
            request.ProductIds is null ? null : empty with { ProductIds = request.ProductIds },
            request.Languages is null ? null : empty with { Languages = request.Languages },
        ];
        return singles.OfType<BriefEditRequest>();
    }

    private static bool Same(List<string>? before, List<string>? after) => (before ?? []).SequenceEqual(after ?? []);

    private async Task<Opened?> OpenAsync(Guid roomId, int version, int briefVersion, bool locked, CancellationToken cancellationToken)
    {
        var room = locked
            ? await workflow.LockedAsync(roomId, cancellationToken)
            : await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == roomId, cancellationToken);
        if (room is null)
        {
            return null;
        }

        if (room.Version != version)
        {
            return new Opened(room, null, (BriefError.Stale, "This campaign changed since you opened it. Refresh and try again."));
        }

        if (Blocker(room) is { } blocker)
        {
            return new Opened(room, null, (BriefError.NotAllowed, blocker));
        }

        var latest = await db.Briefs.AsNoTracking()
            .Where(brief => brief.RoomId == room.Id)
            .OrderByDescending(brief => brief.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return new Opened(room, null, (BriefError.NotAllowed, "The brief isn't ready yet."));
        }

        return latest.Version != briefVersion
            ? new Opened(room, latest, (BriefError.Stale, "The brief changed since you opened it. Refresh and try again."))
            : new Opened(room, latest, null);
    }

    private async Task<Brief> SaveAsync(
        Room room,
        Brief latest,
        BriefContent content,
        IReadOnlyList<string> fields,
        string origin,
        Guid by,
        string? instruction,
        string? model,
        string? promptVersion,
        CancellationToken cancellationToken)
    {
        var brief = new Brief
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            Version = latest.Version + 1,
            Content = content,
            Origin = origin,
            Model = model,
            PromptVersion = promptVersion,
            CreatedBy = by,
            Instruction = instruction,
            CreatedAt = time.GetUtcNow(),
        };
        db.Briefs.Add(brief);
        if (RedesignsTest(fields) && queue.Find(StageKinds.ProofPlan) is not null)
        {
            await queue.EnqueueAsync(room, StageKinds.ProofPlan, by, cancellationToken, await KeptHoldoutsAsync(room, fields, cancellationToken));
        }

        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        return brief;
    }

    private async Task<string?> KeptHoldoutsAsync(Room room, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        if (fields.Contains(BriefFields.Regions) || await ProofPlanReader.LatestAsync(db, room.Id, cancellationToken) is not { } plan)
        {
            return null;
        }

        var content = plan.Read();
        return content.ChosenByUser && content.Holdouts.Count > 0
            ? JsonSerializer.Serialize(new HoldoutChoice([.. content.Holdouts.Select(region => region.Id)]), JsonSerializerOptions.Web)
            : null;
    }

    private async Task FinishAsync(Room room, AuditRecord record, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(record, cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
    }

    private static string? Required(string text, int length)
    {
        var trimmed = text.Trim();
        return trimmed.Length is > 0 && trimmed.Length <= length ? trimmed : null;
    }

    private static List<string>? List(string?[] values, int length, int limit)
    {
        var trimmed = values.Select(value => value?.Trim() ?? "").ToList();
        return trimmed.Count is > 0 && trimmed.Count <= limit && trimmed.All(value => value.Length is > 0 && value.Length <= length)
            ? [.. trimmed.Distinct()]
            : null;
    }

    private static string[]? Strings(IReadOnlyList<string?>? values) => values is null ? null : [.. values.OfType<string>()];

    private static string Cut(string text, int length)
    {
        var trimmed = (text ?? "").Trim();
        return trimmed.Length > length ? trimmed[..length] : trimmed;
    }

    private sealed record Opened(Room Room, Brief? Brief, (BriefError Error, string Message)? Error);
}
