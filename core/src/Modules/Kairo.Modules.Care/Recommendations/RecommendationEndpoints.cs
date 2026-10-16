namespace Kairo.Modules.Care.Recommendations;

internal static class RecommendationEndpoints
{
    public static async Task<Results<Ok<RecommendationList>, NotFound>> ListAsync(Guid campaignId, RecommendationDesk desk, CurrentMember member, CancellationToken cancellationToken) =>
        await desk.ListAsync(campaignId, member, cancellationToken) is { } list ? TypedResults.Ok(list) : TypedResults.NotFound();

    public static async Task<Results<Ok<RecommendationView>, NotFound, ProblemHttpResult, ValidationProblem>> ApproveAsync(
        Guid recommendationId, ApproveRecommendation request, RecommendationDesk desk, CurrentMember member, CancellationToken cancellationToken)
    {
        if (request.Version is not { } version || version < 1) return VersionProblem();
        return Result(await desk.ApproveAsync(recommendationId, version, member, cancellationToken));
    }

    public static async Task<Results<Ok<RecommendationView>, NotFound, ProblemHttpResult, ValidationProblem>> DismissAsync(
        Guid recommendationId, DismissRecommendation request, RecommendationDesk desk, CurrentMember member, CancellationToken cancellationToken)
    {
        if (request.Version is not { } version || version < 1) return VersionProblem();
        if (request.Reason is { Length: > RecommendationDesk.MaxReasonLength })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [$"Keep the reason to {RecommendationDesk.MaxReasonLength} characters or fewer."] });
        return Result(await desk.DismissAsync(recommendationId, version, request.Reason, member, cancellationToken));
    }

    private static ValidationProblem VersionProblem() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Send the recommendation's version from when you opened it."] });

    private static Results<Ok<RecommendationView>, NotFound, ProblemHttpResult, ValidationProblem> Result(DeskResult result) => result.Error switch
    {
        DeskError.None => TypedResults.Ok(result.View!),
        DeskError.NotFound => TypedResults.NotFound(),
        DeskError.Forbidden => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: result.Message),
        DeskError.Google => TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: result.Message),
        _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Message),
    };
}
