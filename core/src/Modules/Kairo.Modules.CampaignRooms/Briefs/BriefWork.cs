using System.Globalization;
using Kairo.Contracts.BrandKit;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class BriefWork(
    CampaignRoomsDbContext db,
    BriefAgent agent,
    BriefDesk desk,
    IBrandKit brandKit,
    TimeProvider time) : IStageWork
{
    public const string RadarSource = "radar";
    public const int RadarDays = 14;
    public const int MaxProducts = 12;
    public const int MaxKeywords = 20;
    public const int MaxRegions = 40;
    public const string Planner = BudgetGuide.Planner;

    public string Kind => StageKinds.Brief;

    public string Agent => StageAgents.Plan;

    public string Stage => RoomStates.BriefDraft;

    public string? FollowUp => StageKinds.ProofPlan;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        var context = await desk.ReadAsync(room, null, false, cancellationToken);
        var evidence = context.Evidence;
        var products = context.Products.Where(product => product.InStock).Take(MaxProducts).ToList();
        if (products.Count == 0)
        {
            throw new StageFailure(StageFailure.NoProducts, "None of this campaign's products are in stock, so Kairo can't draft a brief.");
        }

        var keyed = products.Select((product, index) => (Key: $"p{index + 1}", Product: product)).ToList();
        var (start, end) = SuggestedDates(context);
        var regions = context.Regions.Take(MaxRegions).ToList();
        var guide = BudgetGuide.Estimate(
            evidence,
            [.. regions.Select(region => region.Name)],
            end.DayNumber - start.DayNumber + 1,
            [.. products.Where(product => product.Price is > 0).Select(product => product.Price!.Value)],
            context.Brand.Currency);
        var kit = await brandKit.GetApprovedAsync(cancellationToken);
        var answer = await agent.DraftAsync(
            new SmartBriefRequest(
                context.Brand.Name,
                BriefDesk.Market,
                new SmartOpportunity(
                    Cut(evidence.Topic ?? room.Title, 200),
                    Category(evidence, room),
                    evidence.Trigger?.Source ?? RadarSource,
                    Summary(evidence, room),
                    [.. (evidence.Keywords ?? []).Where(keyword => !string.IsNullOrWhiteSpace(keyword)).Select(keyword => Cut(keyword, 100)).Distinct().Take(MaxKeywords)],
                    evidence.LastMonthSearches,
                    evidence.Growth,
                    [.. regions.Select(region => new SmartRegion(Cut(region.Name, 100), region.LastMonthSearches, Growth(evidence, region.Name)))]),
                [.. keyed.Select(entry => new SmartProduct(entry.Key, Cut(entry.Product.Title, 150), entry.Product.Price, entry.Product.Currency))],
                new SmartBudgetGuide(guide.Low, guide.High, guide.Suggested, guide.Currency, guide.Days, Cut(guide.Basis, 500)),
                new SmartDateGuide(BriefDesk.Iso(context.Today), BriefDesk.Iso(start), BriefDesk.Iso(end), context.LatestEnd is { } latest ? BriefDesk.Iso(latest) : null),
                [.. BriefChoices.Languages.Select(language => language.Value)],
                Seed(evidence.Brief),
                [],
                kit?.Content),
            cancellationToken);
        if (Clean(answer.Brief, context, keyed, guide, start, end) is not { } content)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Plan agent's brief is missing required fields.");
        }

        return new StageOutcome(content, answer.Model, answer.PromptVersion, answer.Usage, "Drafted the brief");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var latest = await db.Briefs.Where(brief => brief.RoomId == room.Id).MaxAsync(brief => (int?)brief.Version, cancellationToken) ?? 0;
        db.Briefs.Add(new Brief
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            Version = latest + 1,
            Content = (BriefContent)outcome.Output,
            Origin = BriefOrigins.Drafted,
            StageRunId = run.Id,
            Model = outcome.Model,
            PromptVersion = outcome.PromptVersion,
            CreatedAt = time.GetUtcNow(),
        });
    }

    public static (DateOnly Start, DateOnly End) SuggestedDates(BriefDeskContext context)
    {
        var start = context.Today.AddDays(1);
        if (context.LatestEnd is not { } latest)
        {
            return (start, start.AddDays(RadarDays - 1));
        }

        if (latest < start)
        {
            start = context.Today;
        }

        var end = latest < start ? start : latest;
        var longest = start.AddDays(BriefChecks.MaxDays - 1);
        return (start, end > longest ? longest : end);
    }

    public static BriefContent? Clean(
        SmartDraft? draft,
        BriefDeskContext context,
        IReadOnlyList<(string Key, BriefProductOption Product)> keyed,
        BudgetEstimate guide,
        DateOnly suggestedStart,
        DateOnly suggestedEnd)
    {
        if (draft is null
            || Text(draft.Title, BriefLimits.Title) is not { } title
            || Text(draft.Objective, BriefLimits.Objective) is not { } objective)
        {
            return null;
        }

        var audiences = Texts(draft.Audiences, BriefLimits.Audience, BriefLimits.Audiences);
        var messages = Texts(draft.KeyMessages, BriefLimits.KeyMessage, BriefLimits.KeyMessages);
        if (audiences.Count == 0 || messages.Count == 0)
        {
            return null;
        }

        var kpi = draft.Kpi?.Trim().ToLowerInvariant() is { } chosenKpi && BriefChoices.IsKpi(chosenKpi) ? chosenKpi : "orders";
        var types = (draft.CampaignTypes ?? []).OfType<string>()
            .Select(type => type.Trim().ToLowerInvariant())
            .Where(BriefChoices.IsCampaignType)
            .Distinct()
            .ToList();
        var regions = (draft.Regions ?? []).OfType<string>().Select(context.Region).OfType<string>().Distinct().ToList();
        var products = (draft.ProductIds ?? []).OfType<string>()
            .Select(key => keyed.FirstOrDefault(entry => entry.Key == key.Trim()).Product)
            .OfType<BriefProductOption>()
            .DistinctBy(product => product.Id)
            .Take(BriefLimits.Products)
            .ToList();
        if (products.Count == 0)
        {
            products = [.. keyed.Take(3).Select(entry => entry.Product)];
        }

        var languages = (draft.Languages ?? []).OfType<string>()
            .Select(language => language.Trim().ToLowerInvariant())
            .Where(BriefChoices.IsLanguage)
            .Distinct()
            .Take(BriefLimits.Languages)
            .ToList();
        var (start, end) = Dates(BriefDesk.ParseDay(draft.StartDate), BriefDesk.ParseDay(draft.EndDate), context, suggestedStart, suggestedEnd);
        var content = new BriefContent
        {
            Title = title,
            Objective = objective,
            Kpi = kpi,
            CampaignTypes = types.Count > 0 ? types : [BriefChoices.Search],
            Budget = new BriefBudget { Amount = guide.Clamp(draft.Budget is decimal amount && amount > 0 ? amount : guide.Suggested), Currency = guide.Currency },
            StartDate = start,
            EndDate = end,
            Regions = regions.Count > 0 ? regions : [.. context.Regions.Take(MaxRegions).Select(region => region.Name)],
            Audiences = audiences,
            KeyMessages = messages,
            ProductIds = [.. products.Select(product => product.Id)],
            LandingPages = LandingPages(products),
            Languages = languages.Count > 0 ? languages : ["en"],
        };
        foreach (var field in BriefFields.All.Where(field => field != BriefFields.Title))
        {
            var reason = field == BriefFields.LandingPages
                ? "The product pages of the chosen products."
                : Text(draft.Reasons?.For(field), BriefLimits.Reason) ?? "";
            content.SetReason(field, reason, Evidence(field, content, context, guide));
        }

        return content;
    }

    public static (DateOnly Start, DateOnly End) Dates(DateOnly? start, DateOnly? end, BriefDeskContext context, DateOnly suggestedStart, DateOnly suggestedEnd)
    {
        var chosenStart = start is { } day && day >= context.Today ? day : suggestedStart;
        var chosenEnd = end is { } last && last >= chosenStart ? last : (suggestedEnd >= chosenStart ? suggestedEnd : chosenStart);
        if (context.LatestEnd is { } latest)
        {
            if (chosenStart > latest)
            {
                chosenStart = latest < context.Today ? context.Today : latest;
            }

            if (chosenEnd > latest)
            {
                chosenEnd = latest < chosenStart ? chosenStart : latest;
            }
        }

        if (chosenEnd.DayNumber - chosenStart.DayNumber + 1 > BriefChecks.MaxDays)
        {
            chosenEnd = chosenStart.AddDays(BriefChecks.MaxDays - 1);
        }

        return (chosenStart, chosenEnd);
    }

    public static List<string> LandingPages(IEnumerable<BriefProductOption> products) =>
        products.Select(product => product.Link)
            .OfType<string>()
            .Where(BriefLimits.IsLink)
            .Distinct(StringComparer.Ordinal)
            .Take(BriefLimits.LandingPages)
            .ToList();

    public static IEnumerable<BriefEvidence> Evidence(string field, BriefContent content, BriefDeskContext context, BudgetEstimate guide)
    {
        var evidence = context.Evidence;
        switch (field)
        {
            case BriefFields.Objective:
                if (evidence.Trigger?.Summary is { Length: > 0 } triggered)
                {
                    yield return new BriefEvidence { Label = "Trigger", Value = Cut(triggered, 300), Source = evidence.Trigger.Source };
                }

                if (evidence.LastMonthSearches is long searches)
                {
                    yield return new BriefEvidence { Label = "Searches last month", Value = BudgetGuide.Count(searches), Source = Planner };
                }

                if (evidence.Growth is double growth)
                {
                    yield return new BriefEvidence { Label = "Growth", Value = Percent(growth), Source = Planner };
                }

                break;
            case BriefFields.CampaignTypes:
                if (evidence.Keywords is { Length: > 0 } keywords)
                {
                    yield return new BriefEvidence { Label = "Rising keywords", Value = string.Join(", ", keywords.Take(5)), Source = Planner };
                }

                yield return new BriefEvidence
                {
                    Label = "Products with prices",
                    Value = content.ProductIds.Count(id => context.Product(id)?.Price is > 0).ToString(CultureInfo.InvariantCulture),
                    Source = BudgetGuide.Catalogue,
                };
                break;
            case BriefFields.Budget:
                yield return new BriefEvidence { Label = "Suggested range", Value = $"{BudgetGuide.Money(guide.Low, guide.Currency)} to {BudgetGuide.Money(guide.High, guide.Currency)}" };
                foreach (var item in guide.Evidence)
                {
                    yield return item;
                }

                break;
            case BriefFields.Dates:
                if (context.LatestEnd is { } latest)
                {
                    yield return new BriefEvidence { Label = "The moment ends", Value = BriefChecks.Day(latest), Source = evidence.Trigger?.Source };
                }
                else
                {
                    yield return new BriefEvidence { Label = "Usual length", Value = $"{RadarDays} days" };
                }

                break;
            case BriefFields.Regions:
                foreach (var name in content.Regions)
                {
                    var region = context.Regions.FirstOrDefault(option => option.Name == name);
                    if (region?.LastMonthSearches is long regional)
                    {
                        var regionGrowth = Growth(evidence, name);
                        yield return new BriefEvidence
                        {
                            Label = name,
                            Value = regionGrowth is double change ? $"{BudgetGuide.Count(regional)} searches last month, {Percent(change)}" : $"{BudgetGuide.Count(regional)} searches last month",
                            Source = Planner,
                        };
                    }
                }

                if (evidence.Trigger?.Summary is { Length: > 0 } where)
                {
                    yield return new BriefEvidence { Label = "Trigger", Value = Cut(where, 300), Source = evidence.Trigger.Source };
                }

                break;
            case BriefFields.Products:
                foreach (var id in content.ProductIds)
                {
                    if (context.Product(id) is { } product)
                    {
                        yield return new BriefEvidence
                        {
                            Label = product.Title,
                            Value = product.Price is { } price ? BudgetGuide.Money(price, product.Currency ?? guide.Currency) : "In stock",
                            Source = BudgetGuide.Catalogue,
                        };
                    }
                }

                break;
        }
    }

    public static string Percent(double change) =>
        change >= 0 ? $"up {BudgetGuide.Share(change)}" : $"down {BudgetGuide.Share(-change)}";

    private static double? Growth(RoomEvidence evidence, string region) =>
        (evidence.Regions ?? []).FirstOrDefault(entry => string.Equals(entry.Name?.Trim(), region, StringComparison.OrdinalIgnoreCase))?.Growth;

    private static SmartSeed? Seed(EvidenceBrief? brief) => brief is null || string.IsNullOrWhiteSpace(brief.Title)
        ? null
        : new SmartSeed(Cut(brief.Title, 200), CutOrNull(brief.Goal, 500), CutOrNull(brief.Audience, 500), CutOrNull(brief.KeyMessage, 500), CutOrNull(brief.Offer, 300));

    private static string Category(RoomEvidence evidence, Room room)
    {
        var text = evidence.Categories is { Length: > 0 } categories ? string.Join(", ", categories) : evidence.Topic ?? room.Title;
        return Cut(text, 750);
    }

    private static string Summary(RoomEvidence evidence, Room room)
    {
        if (evidence.Trigger?.Summary is { Length: > 0 } triggered)
        {
            return Cut(triggered, 500);
        }

        var topic = evidence.Topic ?? room.Title;
        return Cut(string.IsNullOrWhiteSpace(evidence.Reason) ? $"Searches for {topic} are rising." : $"Searches for {topic} are rising. {evidence.Reason.Trim()}", 500);
    }

    public static string? Text(string? value, int length)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > length ? trimmed[..length] : trimmed;
    }

    public static List<string> Texts(IEnumerable<string?>? values, int length, int limit) =>
        (values ?? []).Select(value => Text(value, length)).OfType<string>().Distinct().Take(limit).ToList();

    private static string Cut(string text, int length)
    {
        var trimmed = text.Trim();
        return trimmed.Length > length ? trimmed[..length] : trimmed;
    }

    private static string? CutOrNull(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : Cut(text, length);
}

internal sealed class StarterBriefAlias(BriefWork work) : IStageWork
{
    public string Kind => StageKinds.StarterBrief;

    public string Agent => work.Agent;

    public string Stage => work.Stage;

    public string? FollowUp => work.FollowUp;

    public Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken) => work.RunAsync(run, room, cancellationToken);

    public Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken) => work.SaveAsync(run, room, outcome, cancellationToken);
}

internal static class BriefLimits
{
    public const int Title = 120;
    public const int Objective = 300;
    public const int Audience = 200;
    public const int Audiences = 5;
    public const int KeyMessage = 150;
    public const int KeyMessages = 5;
    public const int Regions = 40;
    public const int Products = 12;
    public const int LandingPages = 10;
    public const int Link = 500;
    public const int Languages = 5;
    public const int Reason = 500;
    public const int Instruction = 500;
    public const decimal MaxBudget = 999_999_999_999m;

    public static bool IsLink(string text) =>
        text.Length <= Link
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
