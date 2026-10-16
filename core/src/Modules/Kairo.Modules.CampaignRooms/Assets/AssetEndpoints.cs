using Kairo.Modules.CampaignRooms.Briefs;

namespace Kairo.Modules.CampaignRooms.Assets;

internal static class AssetEndpoints
{
    public static async Task<Results<Ok<AssetSetView>, NotFound, ValidationProblem>> ListAsync(
        Guid campaignId,
        string? language,
        AssetEditor editor,
        CancellationToken cancellationToken)
    {
        if (language is not null && !BriefChoices.IsLanguage(language))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["language"] = ["Choose one of the brief's languages."] });
        }

        return await editor.ListAsync(campaignId, language, cancellationToken) is { } set ? TypedResults.Ok(set) : TypedResults.NotFound();
    }

    public static async Task<Results<Ok<AssetHistoryView>, NotFound>> HistoryAsync(
        Guid campaignId,
        Guid assetId,
        AssetHistory history,
        CancellationToken cancellationToken) =>
        await history.ReadAsync(campaignId, assetId, cancellationToken) is { } view ? TypedResults.Ok(view) : TypedResults.NotFound();

    public static async Task<Results<Ok<AssetView>, NotFound, ProblemHttpResult, ValidationProblem>> EditAsync(
        Guid campaignId,
        Guid assetId,
        AssetEditRequest request,
        AssetEditor editor,
        CancellationToken cancellationToken)
    {
        if (Version(request.Version) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Result(await editor.EditAsync(campaignId, assetId, request, cancellationToken));
    }

    public static Task<Results<Ok<AssetView>, NotFound, ProblemHttpResult, ValidationProblem>> LockAsync(
        Guid campaignId,
        Guid assetId,
        AssetVersionRequest request,
        AssetEditor editor,
        CancellationToken cancellationToken) =>
        SetLockAsync(campaignId, assetId, request, true, editor, cancellationToken);

    public static Task<Results<Ok<AssetView>, NotFound, ProblemHttpResult, ValidationProblem>> UnlockAsync(
        Guid campaignId,
        Guid assetId,
        AssetVersionRequest request,
        AssetEditor editor,
        CancellationToken cancellationToken) =>
        SetLockAsync(campaignId, assetId, request, false, editor, cancellationToken);

    public static async Task<Results<Ok<AssetRegenerationView>, NotFound, ProblemHttpResult, ValidationProblem>> RegenerateAsync(
        Guid campaignId,
        Guid assetId,
        AssetRegenerateRequest request,
        AssetEditor editor,
        CancellationToken cancellationToken)
    {
        var errors = Version(request.Version) ?? [];
        var instruction = AdLimits.Tidy(request.Instruction);
        if (instruction.Length is < AssetLimits.MinInstruction or > AssetLimits.Instruction)
        {
            errors["instruction"] = [$"Say what to change, in {AssetLimits.MinInstruction} to {AssetLimits.Instruction} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Result(await editor.RegenerateAsync(campaignId, assetId, request.Version!.Value, instruction, cancellationToken));
    }

    private static async Task<Results<Ok<AssetView>, NotFound, ProblemHttpResult, ValidationProblem>> SetLockAsync(
        Guid campaignId,
        Guid assetId,
        AssetVersionRequest request,
        bool locked,
        AssetEditor editor,
        CancellationToken cancellationToken)
    {
        if (Version(request.Version) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Result(await editor.LockAsync(campaignId, assetId, request.Version!.Value, locked, cancellationToken));
    }

    private static Dictionary<string, string[]>? Version(int? version) =>
        version is { } value && value >= 1
            ? null
            : new Dictionary<string, string[]> { ["version"] = ["Send the asset's version from when you opened it."] };

    public static Results<Ok<T>, NotFound, ProblemHttpResult, ValidationProblem> Result<T>(AssetResult<T> result)
        where T : class => result.Error switch
        {
            AssetError.None => TypedResults.Ok(result.Value!),
            AssetError.NotFound => TypedResults.NotFound(),
            AssetError.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Message),
        };
}
