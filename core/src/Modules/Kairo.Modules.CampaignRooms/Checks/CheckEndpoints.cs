using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Checks;

internal static class CheckEndpoints
{
    public static async Task<Results<Ok<AssetView>, NotFound, ProblemHttpResult, ValidationProblem>> OverrideAsync(
        Guid campaignId,
        Guid assetId,
        CheckOverrideRequest request,
        CheckReview review,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Version is not >= 1)
        {
            errors["version"] = ["Send the asset's version from when you opened it."];
        }

        var reason = CheckScoring.Cut(request.Reason, int.MaxValue);
        if (reason.Length is < CheckResultLimits.MinReason or > CheckResultLimits.MaxReason)
        {
            errors["reason"] = [$"Say why the flag can be overridden, in {CheckResultLimits.MinReason} to {CheckResultLimits.MaxReason} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return AssetEndpoints.Result(await review.OverrideAsync(campaignId, assetId, request.Version!.Value, reason, cancellationToken));
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> SendBackAsync(
        Guid campaignId,
        AssetSendBackRequest request,
        RoomWorkflow workflow,
        CampaignReader reader,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Version is not >= 1)
        {
            errors["version"] = ["Send the campaign's version from when you opened it."];
        }

        var comment = request.Comment?.Trim() ?? "";
        if (comment.Length is < RoomLimits.MinCommentLength or > AssetLimits.Instruction)
        {
            errors["comment"] = [$"Say what needs to change, in {RoomLimits.MinCommentLength} to {AssetLimits.Instruction} characters."];
        }

        var ids = (request.AssetIds ?? []).Distinct().ToList();
        if (ids.Count is 0 or > CheckResultLimits.MaxSendBack || ids.Contains(Guid.Empty))
        {
            errors["assetIds"] = [$"Choose 1 to {CheckResultLimits.MaxSendBack} assets to send back."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await CampaignEndpoints.ResultAsync(
            await workflow.SendBackAssetsAsync(campaignId, ids, comment, request.Version!.Value, member, cancellationToken),
            reader,
            cancellationToken);
    }

    public static async Task<Results<Ok<CampaignDetail>, NotFound, ProblemHttpResult, ValidationProblem>> RequestCheckAsync(
        Guid campaignId,
        AssetCheckRequest request,
        CheckReview review,
        CampaignReader reader,
        CancellationToken cancellationToken)
    {
        if (request.Version is not >= 1)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Send the campaign's version from when you opened it."] });
        }

        return await CampaignEndpoints.ResultAsync(await review.RequestCheckAsync(campaignId, request.Version.Value, cancellationToken), reader, cancellationToken);
    }
}
