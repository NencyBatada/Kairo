using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed class ImageRegenerator(MediaDesk desk, MediaAgent agent) : IAssetRegenerator
{
    public IReadOnlyCollection<string> Kinds => [AssetKinds.Image];

    public async Task<AssetRegeneration> RegenerateAsync(AssetRegenerationContext context, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(context.Room, cancellationToken) is not { } copy)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to make images from.");
        }

        var product = copy.Products.FirstOrDefault(entry => context.Asset.ProductIds.Contains(entry.Product.Id.ToString()))
            ?? throw new StageFailure(StageFailure.NoProducts, "This image's product is no longer in stock.");
        var photos = await desk.PhotosAsync([product], 1, cancellationToken);
        if (photos.Count == 0)
        {
            throw MediaDesk.NoPhotos();
        }

        var format = context.Asset.Format ?? AssetFormats.Square;
        var shot = new ImageShot("s1", product.Key, ImagesWork.StyleOf(context.Asset), [format]);
        var answer = await agent.ImagesAsync(
            new ImagesRequest(
                MediaDesk.Brief(copy),
                copy.BrandKit,
                [new MediaReferenceProduct(product.Key, CopyDesk.Cut(product.Product.Title, 300), CopyDesk.Cut(product.Product.Description, 2000), photos[0].Photos)],
                [shot],
                context.Instruction),
            cancellationToken);
        if ((answer.Images ?? []).FirstOrDefault(image => image.ShotId == shot.Id && image.Format == format && image.Data.Length > 0) is not { } image)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Create agent didn't return the image.");
        }

        var next = context.Content.Copy();
        next.Media = await desk.SaveAsync(context.Room.BrandId, image.Data, "image/jpeg", cancellationToken);
        return new AssetRegeneration(next, answer.Model, answer.PromptVersion, answer.Usage, "Made a new version of the image.");
    }
}

internal sealed class VideoRegenerator(MediaDesk desk, VideoMaker maker) : IAssetRegenerator
{
    public IReadOnlyCollection<string> Kinds => [AssetKinds.Video];

    public async Task<AssetRegeneration> RegenerateAsync(AssetRegenerationContext context, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(context.Room, cancellationToken) is not { } copy)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to make a video from.");
        }

        var language = context.Asset.Language;
        var newFootage = VideoMaker.NeedsNewFootage(context.Instruction);
        var made = await maker.MakeAsync(context.Room, copy with { Language = language }, context.Instruction, !newFootage, cancellationToken);
        var next = context.Content.Copy();
        next.Media = made.Video.Content.Media;
        next.Script = made.Video.Content.Script;
        return new AssetRegeneration(
            next,
            made.Model,
            made.PromptVersion,
            made.Usage,
            made.Footage.Count > 0 ? "Made new footage and a new video." : "Made a new video from the same footage.",
            [made.Voiceover, .. made.Footage]);
    }
}
