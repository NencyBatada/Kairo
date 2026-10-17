using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Measurement.Lab;

internal static class LabEndpoints
{
    public const int ListLimit = 100;
    public const int NameLength = 200;
    public const int ChangeLength = 1000;
    public const string MemorySource = "impact_lab";
    public const string Changed = "This analysis changed since you opened it. Refresh and try again.";
    public const string Busy = "This analysis is running. Wait for it to finish.";
    public const string NotLoaded = "Load the data before picking series.";
    public const string NotChecked = "Pick the affected and comparison series before running.";
    public const string NotConclusive = "Only a conclusive result can be saved to Brand Memory.";
    public const string AlreadySaved = "This result is already in Brand Memory.";
    public const string NotFile = "This analysis doesn't use an uploaded file.";
    public const string NeedsVersion = "Send the analysis version from when you opened it.";

    public static string LockName(Guid analysisId) => $"impact-lab:{analysisId:N}";

    public static async Task<Ok<LabAnalysisList>> ListAsync(MeasurementDbContext db, CancellationToken cancellationToken)
    {
        var analyses = await db.LabAnalyses.AsNoTracking().OrderByDescending(row => row.CreatedAt).Take(ListLimit).ToListAsync(cancellationToken);
        return TypedResults.Ok(new LabAnalysisList([.. analyses.Select(LabViews.Summary)]));
    }

    public static async Task<Results<Ok<LabAnalysisView>, NotFound>> GetAsync(Guid analysisId, MeasurementDbContext db, CancellationToken cancellationToken)
    {
        if (await db.LabAnalyses.AsNoTracking().SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } analysis)
        {
            return TypedResults.NotFound();
        }

        var dataset = await db.LabDatasets.AsNoTracking().SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        return TypedResults.Ok(LabViews.Of(analysis, dataset));
    }

    public static async Task<Results<Created<LabAnalysisView>, ValidationProblem>> CreateAsync(
        LabSetupRequest request,
        MeasurementDbContext db,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (spec, errors) = Validate(request, time);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = time.GetUtcNow();
        var analysis = new LabAnalysis
        {
            Id = Guid.NewGuid(),
            CreatedBy = member.UserId,
            CreatedAt = now,
            UpdatedAt = now,
            State = LabStates.Setup,
        };
        Apply(analysis, request, spec!);
        db.LabAnalyses.Add(analysis);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("impact_lab.analysis_created", LabViews.AuditTarget, analysis.Id.ToString(),
                After: new { analysis.Name, analysis.Start, analysis.End, analysis.AffectsKind, source = spec }),
            cancellationToken);
        await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/impact-lab/analyses/{analysis.Id}", LabViews.Of(analysis, null));
    }

    public static async Task<Results<Ok<LabAnalysisView>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid analysisId,
        LabSetupRequest request,
        MeasurementDbContext db,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (spec, errors) = Validate(request, time);
        if (request.Version is not { } version || version < 1)
        {
            errors["version"] = [NeedsVersion];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } analysis)
        {
            return TypedResults.NotFound();
        }

        if (Refuse(analysis, request.Version) is { } refused)
        {
            return refused;
        }

        var before = LabSourceSpec.Read(analysis.Source);
        var reset = before != spec || analysis.Start != request.Start || analysis.End != request.End;
        Apply(analysis, request, spec!);
        var dataset = await db.LabDatasets.SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        if (reset)
        {
            ResetData(analysis, dataset, keepUpload: before.Kind == LabKinds.File && spec!.Kind == LabKinds.File);
        }

        analysis.Version++;
        analysis.UpdatedAt = time.GetUtcNow();
        if (await SaveAsync(db, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
        return TypedResults.Ok(LabViews.Of(analysis, dataset));
    }

    public static async Task<Results<Ok<LabAnalysisView>, NotFound, ValidationProblem, ProblemHttpResult>> UploadAsync(
        Guid analysisId,
        int? version,
        IFormFile? file,
        MeasurementDbContext db,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return FileProblem([SalesEndpoints.ChooseFile]);
        }

        if (file.Length > SalesEndpoints.MaxFileBytes)
        {
            return FileProblem([SalesEndpoints.TooLarge]);
        }

        if (await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } analysis)
        {
            return TypedResults.NotFound();
        }

        if (Refuse(analysis, version) is { } refused)
        {
            return refused;
        }

        if (LabSourceSpec.Read(analysis.Source).Kind != LabKinds.File)
        {
            return Conflict(NotFile);
        }

        LabFile parsed;
        await using (var stream = file.OpenReadStream())
        {
            parsed = LabCsv.Parse(stream, FeedClock.Today(time, FeedClock.IndiaTimeZone));
        }

        if (!parsed.IsValid)
        {
            return FileProblem(parsed.Errors);
        }

        var dataset = await db.LabDatasets.SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        if (dataset is null)
        {
            dataset = new LabDataset { AnalysisId = analysisId };
            db.LabDatasets.Add(dataset);
        }

        var name = Path.GetFileName(file.FileName);
        ResetData(analysis, dataset, keepUpload: false);
        dataset.FileName = name.Length == 0 ? "outcome.csv" : name.Length > 200 ? name[..200] : name;
        dataset.UploadFrom = parsed.Data!.From;
        dataset.UploadTo = parsed.Data.To;
        dataset.Upload = LabViews.Write(parsed.Data.Series);
        dataset.UpdatedAt = time.GetUtcNow();
        analysis.Version++;
        analysis.UpdatedAt = dataset.UpdatedAt;
        if (await SaveAsync(db, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
        return TypedResults.Ok(LabViews.Of(analysis, dataset));
    }

    public static async Task<Results<Ok<LabAnalysisView>, NotFound, ValidationProblem, ProblemHttpResult>> LoadAsync(
        Guid analysisId,
        LabVersionRequest request,
        MeasurementDbContext db,
        KairoDatabase database,
        LabWork work,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.LabAnalyses.AsNoTracking().SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } current)
        {
            return TypedResults.NotFound();
        }

        if (Refuse(current, request.Version) is { } refused)
        {
            return refused;
        }

        var product = LabKinds.Product(LabSourceSpec.Read(current.Source).Kind);
        LabData data;
        try
        {
            data = await work.LoadAsync(current, cancellationToken);
        }
        catch (LabDataProblem problem)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["data"] = [problem.Message] });
        }
        catch (ConnectionUnavailableException unavailable)
        {
            return Conflict(LabWork.ConnectFirst(unavailable.Product));
        }
        catch (ConnectionAccessLostException)
        {
            return Conflict(LabWork.Reconnect(product ?? ConnectedProduct.BigQuery));
        }
        catch (GoogleApiException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Google couldn't return the data.", detail: exception.Message);
        }

        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(analysisId), cancellationToken);
            var analysis = await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken);
            if (analysis is null || analysis.Version != current.Version || analysis.State == LabStates.Running)
            {
                await database.RollbackAsync(CancellationToken.None);
                return analysis is null ? TypedResults.NotFound() : Conflict(analysis.State == LabStates.Running ? Busy : Changed);
            }

            var dataset = await work.DatasetAsync(analysisId, cancellationToken);
            dataset.From = data.From;
            dataset.To = data.To;
            dataset.Currency = data.Currency;
            dataset.Series = LabViews.Write(data.Series);
            dataset.UpdatedAt = time.GetUtcNow();
            LabWork.ClearSelection(analysis);
            analysis.DataFrom = data.From;
            analysis.DataTo = data.To;
            analysis.HistoryDays = analysis.Start.DayNumber - data.From.DayNumber;
            analysis.ChangeDays = data.To.DayNumber - analysis.Start.DayNumber + 1;
            analysis.SeriesCount = data.Series.Count;
            analysis.State = LabStates.DataCheck;
            analysis.Version++;
            analysis.UpdatedAt = dataset.UpdatedAt;
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord("impact_lab.data_loaded", LabViews.AuditTarget, analysis.Id.ToString(),
                    After: new { data.From, data.To, series = data.Series.Count, analysis.HistoryDays, analysis.ChangeDays }),
                cancellationToken);
            await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
            await database.CommitAsync(cancellationToken);
            return TypedResults.Ok(LabViews.Of(analysis, dataset));
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public static async Task<Results<Ok<LabAnalysisView>, NotFound, ValidationProblem, ProblemHttpResult>> SelectAsync(
        Guid analysisId,
        LabSelectionRequest request,
        MeasurementDbContext db,
        KairoDatabase database,
        LabWork work,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.LabAnalyses.AsNoTracking().SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } current)
        {
            return TypedResults.NotFound();
        }

        if (Refuse(current, request.Version) is { } refused)
        {
            return refused;
        }

        var stored = await db.LabDatasets.AsNoTracking().Where(row => row.AnalysisId == analysisId).Select(row => row.Series).SingleOrDefaultAsync(cancellationToken);
        if (current.State == LabStates.Setup || stored is null || current.HistoryDays is null || current.ChangeDays is null)
        {
            return Conflict(NotLoaded);
        }

        var series = LabViews.Read<LabSeriesData[]>(stored) ?? [];
        var problems = LabWork.Problems(request, series.Select(item => item.Key).ToHashSet(StringComparer.Ordinal), out var affected, out var comparison);
        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["affected"] = [.. problems] });
        }

        LabCheckAnswer answer;
        try
        {
            answer = await work.CheckAsync(current, series, affected, comparison, cancellationToken);
        }
        catch (AgentCallException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Kairo couldn't check the comparison. Try again.", detail: exception.Code);
        }

        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(analysisId), cancellationToken);
            var analysis = await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken);
            if (analysis is null || analysis.Version != current.Version || analysis.State == LabStates.Running)
            {
                await database.RollbackAsync(CancellationToken.None);
                return analysis is null ? TypedResults.NotFound() : Conflict(analysis.State == LabStates.Running ? Busy : Changed);
            }

            var keys = series.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            LabWork.ClearResult(analysis);
            analysis.Affected = LabViews.Write(affected);
            analysis.Comparison = LabViews.Write(answer.Comparison.Where(keys.Contains).ToArray());
            analysis.Candidates = LabViews.Write(answer.Candidates.Where(candidate => keys.Contains(candidate.Key)).ToArray());
            analysis.Fit = answer.Fit;
            analysis.Detectable = answer.Detectable;
            analysis.Placebos = answer.Placebos;
            analysis.Warnings = LabViews.Write(answer.Warnings);
            analysis.State = LabStates.DataCheck;
            analysis.Version++;
            analysis.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
            await database.CommitAsync(cancellationToken);
            var dataset = await db.LabDatasets.AsNoTracking().SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
            return TypedResults.Ok(LabViews.Of(analysis, dataset));
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public static async Task<Results<Accepted<LabAnalysisView>, NotFound, ProblemHttpResult>> RunAsync(
        Guid analysisId,
        LabVersionRequest request,
        MeasurementDbContext db,
        CurrentMember member,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } analysis)
        {
            return TypedResults.NotFound();
        }

        if (Refuse(analysis, request.Version) is { } refused)
        {
            return refused;
        }

        if (analysis.State is LabStates.Setup || LabViews.Keys(analysis.Affected).Length == 0 || LabViews.Keys(analysis.Comparison).Length == 0)
        {
            return Conflict(NotChecked);
        }

        var now = time.GetUtcNow();
        LabWork.ClearResult(analysis);
        analysis.State = LabStates.Running;
        analysis.Attempts = 0;
        analysis.ClaimedUntil = null;
        analysis.RunBy = member.UserId;
        analysis.RunAt = now;
        analysis.Version++;
        analysis.UpdatedAt = now;
        if (await SaveAsync(db, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
        var dataset = await db.LabDatasets.AsNoTracking().SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        return TypedResults.Accepted($"/api/brands/{member.BrandId}/impact-lab/analyses/{analysis.Id}", LabViews.Of(analysis, dataset));
    }

    public static async Task<Results<Ok<LabAnalysisView>, NotFound, ProblemHttpResult>> RememberAsync(
        Guid analysisId,
        LabVersionRequest request,
        MeasurementDbContext db,
        KairoDatabase database,
        IBrandMemory memory,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await database.LockAsync(LockName(analysisId), cancellationToken);
        if (await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, cancellationToken) is not { } analysis)
        {
            return TypedResults.NotFound();
        }

        if (Refuse(analysis, request.Version) is { } refused)
        {
            return refused;
        }

        if (analysis.MemoryEntryId is not null)
        {
            return Conflict(AlreadySaved);
        }

        if (analysis is not { State: LabStates.Result, Conclusive: true, Outcome: LabOutcomes.Rose or LabOutcomes.Fell })
        {
            return Conflict(NotConclusive);
        }

        var dataset = await db.LabDatasets.AsNoTracking().SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        var spec = LabSourceSpec.Read(analysis.Source);
        var names = (LabViews.Read<LabSeriesData[]>(dataset?.Series ?? "[]") ?? []).ToDictionary(item => item.Key, item => item.Name, StringComparer.Ordinal);
        var affected = LabViews.Keys(analysis.Affected).Select(key => names.GetValueOrDefault(key, key)).ToList();
        var figures = LabViews.Read<LabStoredResult>(analysis.Result!)?.Figures;
        var tags = Tags(spec, analysis.AffectsKind);
        var entryId = await memory.SaveAsync(
            new MemoryDraft(
                MemoryKinds.Change,
                analysis.Outcome == LabOutcomes.Rose ? MemoryOutcomes.Worked : MemoryOutcomes.DidntWork,
                analysis.Name,
                analysis.Summary ?? analysis.WhatChanged,
                LabViews.Write(new
                {
                    labAnalysisId = analysis.Id,
                    figures?.Lift,
                    figures?.Low,
                    figures?.High,
                    figures?.Extra,
                    metric = spec.Metric,
                    source = spec.Kind,
                    start = analysis.Start,
                    end = analysis.DataTo ?? analysis.End,
                }),
                tags == Tag.Products ? affected : [],
                tags == Tag.Categories ? affected : [],
                tags == Tag.Regions ? affected : [],
                MemorySource),
            cancellationToken);
        analysis.MemoryEntryId = entryId;
        analysis.Version++;
        analysis.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("impact_lab.saved_to_memory", LabViews.AuditTarget, analysis.Id.ToString(), After: new { memoryEntryId = entryId, analysis.Outcome }),
            cancellationToken);
        await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
        return TypedResults.Ok(LabViews.Of(analysis, dataset));
    }

    private enum Tag
    {
        None,
        Regions,
        Categories,
        Products,
    }

    private static Tag Tags(LabSourceSpec spec, string affects) => spec.Grain switch
    {
        LabGrains.State or LabGrains.City => Tag.Regions,
        LabGrains.Category => Tag.Categories,
        LabGrains.Product => Tag.Products,
        LabGrains.Series => affects switch
        {
            LabAffects.Regions => Tag.Regions,
            LabAffects.Products => Tag.Products,
            _ => Tag.None,
        },
        _ => Tag.None,
    };

    private static (LabSourceSpec? Spec, Dictionary<string, string[]> Errors) Validate(LabSetupRequest request, TimeProvider time)
    {
        var (spec, errors) = LabSourceRules.Check(request.Source);
        var name = request.Name?.Trim();
        if (name is not { Length: > 0 and <= NameLength })
        {
            errors["name"] = [$"Name the change in 1 to {NameLength} characters."];
        }

        var change = request.WhatChanged?.Trim();
        if (change is not { Length: > 0 and <= ChangeLength })
        {
            errors["whatChanged"] = [$"Describe what changed in 1 to {ChangeLength} characters."];
        }

        if (request.AffectsKind is null || !LabAffects.All.Contains(request.AffectsKind))
        {
            errors["affectsKind"] = ["Use regions, products or other."];
        }

        var today = FeedClock.Today(time, FeedClock.IndiaTimeZone);
        if (request.Start is not { } start)
        {
            errors["start"] = ["Say when the change started."];
        }
        else if (start >= today)
        {
            errors["start"] = ["The change must have started before today."];
        }

        if (request.End is not { } end)
        {
            errors["end"] = ["Say when the change ended."];
        }
        else if (request.Start is { } first && end < first)
        {
            errors["end"] = ["The end can't be before the start."];
        }
        else if (request.Start is { } begin && end.DayNumber - begin.DayNumber + 1 > LabWork.MostChangeDays)
        {
            errors["end"] = [$"Measure at most {LabWork.MostChangeDays} days of the change."];
        }

        return (spec, errors);
    }

    private static void Apply(LabAnalysis analysis, LabSetupRequest request, LabSourceSpec spec)
    {
        analysis.Name = request.Name!.Trim();
        analysis.WhatChanged = request.WhatChanged!.Trim();
        analysis.Start = request.Start!.Value;
        analysis.End = request.End!.Value;
        analysis.AffectsKind = request.AffectsKind!;
        analysis.Source = spec.Write();
    }

    private static void ResetData(LabAnalysis analysis, LabDataset? dataset, bool keepUpload)
    {
        LabWork.ClearSelection(analysis);
        analysis.DataFrom = null;
        analysis.DataTo = null;
        analysis.HistoryDays = null;
        analysis.ChangeDays = null;
        analysis.SeriesCount = 0;
        analysis.State = LabStates.Setup;
        if (dataset is null)
        {
            return;
        }

        dataset.From = null;
        dataset.To = null;
        dataset.Currency = null;
        dataset.Series = null;
        if (!keepUpload)
        {
            dataset.FileName = null;
            dataset.UploadFrom = null;
            dataset.UploadTo = null;
            dataset.Upload = null;
        }
    }

    private static ProblemHttpResult? Refuse(LabAnalysis analysis, int? version)
    {
        if (version is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: NeedsVersion);
        }

        if (analysis.State == LabStates.Running)
        {
            return Conflict(Busy);
        }

        return version != analysis.Version ? Conflict(Changed) : null;
    }

    private static async Task<ProblemHttpResult?> SaveAsync(MeasurementDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(Changed);
        }
    }

    private static ValidationProblem FileProblem(IEnumerable<string> errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [.. errors] });

    private static ProblemHttpResult Conflict(string title) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}
