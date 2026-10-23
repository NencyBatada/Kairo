using Kairo.Contracts.Ask;
using Kairo.Contracts.Media;

namespace Kairo.Modules.Ask;

internal sealed class AskAccessibleMedia(AskService service) : IAccessibleMedia
{
    public string Kind => "ask_preview";
    public async Task<AccessibleMediaSource?> ResolveAsync(MediaTarget target, CancellationToken cancellationToken)
    {
        if (target.ConversationId is not { } conversation || target.Version != 1 || string.IsNullOrEmpty(target.Name)) return null;
        try
        {
            var proposal = await service.ProposalAsync(conversation, target.Id, cancellationToken);
            var file = await service.PreviewFileAsync(conversation, target.Id, target.Name, cancellationToken);
            var canEdit = service.CanEdit(proposal.Target);
            return new(file.Key, file.ContentType, "en", canEdit, CampaignId: proposal.Target.CampaignId);
        }
        catch (AskResourceException exception) when (exception.Status == 404) { return null; }
    }
}
