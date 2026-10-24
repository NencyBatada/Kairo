namespace Kairo.Modules.CampaignRooms.Briefs;

internal static class BriefEndpoints
{
    public static async Task<Results<Ok<BriefDetail>, NotFound>> GetAsync(
        Guid campaignId,
        BriefEditor editor,
        CancellationToken cancellationToken) =>
        await editor.DetailAsync(campaignId, cancellationToken) is { } detail ? TypedResults.Ok(detail) : TypedResults.NotFound();

    public static async Task<Results<Ok<BriefDetail>, NotFound, ProblemHttpResult, ValidationProblem>> EditAsync(
        Guid campaignId,
        BriefEditRequest request,
        BriefEditor editor,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = Versions(request.Version, request.BriefVersion);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Result(await editor.EditAsync(campaignId, request, member, cancellationToken));
    }

    public static async Task<Results<Ok<BriefProposalView>, NotFound, ProblemHttpResult, ValidationProblem>> ProposeAsync(
        Guid campaignId,
        BriefInstructionRequestBody request,
        BriefEditor editor,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = Versions(request.Version, request.BriefVersion);
        var instruction = request.Instruction?.Trim() ?? "";
        if (instruction.Length is < 2 or > BriefLimits.Instruction)
        {
            errors["instruction"] = [$"Say what to change, in 2 to {BriefLimits.Instruction} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return Result(await editor.ProposeAsync(campaignId, request, member, cancellationToken));
    }

    public static async Task<Results<Ok<BriefDetail>, NotFound, ProblemHttpResult, ValidationProblem>> ApplyAsync(
        Guid campaignId,
        Guid proposalId,
        ApplyProposalRequest request,
        BriefEditor editor,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        if (request.Version is not { } version || version < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Send the campaign's version from when you opened it."] });
        }

        return Result(await editor.ApplyAsync(campaignId, proposalId, version, member, cancellationToken));
    }

    private static Dictionary<string, string[]> Versions(int? version, int? briefVersion)
    {
        var errors = new Dictionary<string, string[]>();
        if (version is not { } campaign || campaign < 0)
        {
            errors["version"] = ["Send the campaign's version from when you opened it."];
        }

        if (briefVersion is not { } brief || brief < 1)
        {
            errors["briefVersion"] = ["Send the brief's version from when you opened it."];
        }

        return errors;
    }

    private static Results<Ok<T>, NotFound, ProblemHttpResult, ValidationProblem> Result<T>(BriefResult<T> result)
        where T : class => result.Error switch
        {
            BriefError.None => TypedResults.Ok(result.Value!),
            BriefError.NotFound => TypedResults.NotFound(),
            BriefError.Invalid => TypedResults.ValidationProblem(result.Errors!),
            BriefError.AgentFailed => TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: result.Message),
            BriefError.GenerationLimitExceeded => TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: result.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "generation_limit_exceeded" }),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Message),
        };
}
