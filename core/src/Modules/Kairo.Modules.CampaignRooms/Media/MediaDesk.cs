using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.Connections;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed record ProductPhotos(KeyedProduct Product, IReadOnlyList<MediaPhoto> Photos);

internal sealed record MediaDraft(int BriefVersion, string Language, List<GeneratedAsset>? Assets, bool Video, int Failed);

internal sealed class MediaDesk(
    CampaignRoomsDbContext db,
    CopyDesk copy,
    CataloguePhotos photos,
    IBrandKit brandKit,
    IConnections connections,
    IFileStore files,
    AssetStore store)
{
    public const string FileArea = "assets";
    public const int PhotosPerProduct = 3;
    public const int BusinessNameLength = 25;

    private static readonly string[] LogoTypes = ["image/png", "image/jpeg", "image/webp"];

    public Task<CopyContext?> ReadAsync(Room room, CancellationToken cancellationToken) => copy.ReadAsync(room, cancellationToken);

    public static MediaBrief Brief(CopyContext context)
    {
        var data = context.BriefData();
        return new MediaBrief(data.Title, data.Objective, data.KeyMessages, data.Audiences);
    }

    public async Task<bool> VideoWantedAsync(CopyContext context, CancellationToken cancellationToken) =>
        context.CampaignTypes.Contains(BriefChoices.DemandGen)
        && (await connections.GetStatusAsync(ConnectedProduct.YouTube, cancellationToken)).State == ConnectionState.Connected;

    public async Task<IReadOnlyList<ProductPhotos>> PhotosAsync(IEnumerable<KeyedProduct> products, int most, CancellationToken cancellationToken)
    {
        var found = new List<ProductPhotos>();
        foreach (var product in products)
        {
            if (found.Count == most)
            {
                break;
            }

            var links = new[] { product.Product.ImageLink }.Concat(product.Product.AdditionalImageLinks ?? []);
            var read = await photos.FetchFirstAsync(links, PhotosPerProduct, cancellationToken);
            if (read.Count > 0)
            {
                found.Add(new ProductPhotos(product, read));
            }
        }

        return found;
    }

    public async Task<AssetMedia> SaveAsync(Guid brandId, byte[] data, string contentType, CancellationToken cancellationToken)
    {
        var key = FileKeys.Draft(brandId, FileArea, Guid.NewGuid());
        await files.PutAsync(FileBuckets.GeneratedMedia, key, data, contentType, cancellationToken);
        return new AssetMedia { FileKey = key, ContentType = contentType, Bytes = data.LongLength };
    }

    public async Task<byte[]?> ReadAsync(AssetMedia media, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, media.FileKey, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (StoredFileMissingException)
        {
            return null;
        }
    }

    public async Task<List<(Asset Asset, AssetContent Content)>> CurrentAsync(Guid roomId, string language, string kind, CancellationToken cancellationToken)
    {
        var assets = await db.Assets.AsNoTracking()
            .Where(asset => asset.RoomId == roomId && asset.Language == language && asset.Kind == kind && asset.Status == AssetStatuses.Active)
            .OrderBy(asset => asset.Key)
            .ToListAsync(cancellationToken);
        var current = new List<(Asset, AssetContent)>();
        foreach (var asset in assets)
        {
            if (await store.CurrentAsync(asset, cancellationToken) is { } version)
            {
                current.Add((asset, version.Read()));
            }
        }

        return current;
    }

    public async Task<MediaPhoto?> LogoAsync(JsonElement? kit, CancellationToken cancellationToken)
    {
        if (kit is not { ValueKind: JsonValueKind.Object } content
            || !content.TryGetProperty("logo", out var logo)
            || logo.ValueKind != JsonValueKind.Object
            || !logo.TryGetProperty("fileId", out var id)
            || id.ValueKind != JsonValueKind.String
            || !Guid.TryParse(id.GetString(), out var fileId))
        {
            return null;
        }

        var file = await brandKit.OpenFileAsync(fileId, cancellationToken);
        return file is not null && LogoTypes.Contains(file.ContentType) && file.Content.LongLength is > 0 and <= CataloguePhotos.MaxBytes
            ? new MediaPhoto(file.ContentType, file.Content)
            : null;
    }

    public static string? Background(JsonElement? kit)
    {
        if (kit is not { ValueKind: JsonValueKind.Object } content
            || !content.TryGetProperty("colours", out var colours)
            || colours.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var hexes = colours.EnumerateArray()
            .Where(colour => colour.ValueKind == JsonValueKind.Object && colour.TryGetProperty("hex", out var hex) && hex.ValueKind == JsonValueKind.String)
            .Select(colour => (Role: colour.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String ? role.GetString() : null, Hex: colour.GetProperty("hex").GetString()!))
            .Where(colour => Regex.IsMatch(colour.Hex, "^#[0-9A-Fa-f]{6}$"))
            .ToList();
        return hexes.FirstOrDefault(colour => colour.Role == "primary").Hex ?? hexes.FirstOrDefault().Hex;
    }

    public async Task<string> BusinessNameAsync(CopyContext context, Guid roomId, CancellationToken cancellationToken)
    {
        var text = await CurrentAsync(roomId, context.Language, AssetKinds.DemandGenText, cancellationToken);
        var name = text.Select(entry => entry.Content.BusinessName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? context.Brand;
        return CopyDesk.Cut(name, BusinessNameLength);
    }

    public static string KeyFor(string kind, string language, string discriminator) => CopyWork.KeyFor(kind, language, discriminator);

    public static AgentUsage? Sum(params AgentUsage?[] usages)
    {
        var counted = usages.OfType<AgentUsage>().ToList();
        if (counted.Count == 0)
        {
            return null;
        }

        var inputs = counted.Select(usage => usage.InputTokens).OfType<int>().ToList();
        var outputs = counted.Select(usage => usage.OutputTokens).OfType<int>().ToList();
        return new AgentUsage(inputs.Count == 0 ? null : inputs.Sum(), outputs.Count == 0 ? null : outputs.Sum());
    }

    public static StageFailure NoPhotos() =>
        new(StageFailure.NoPhotos, "Kairo couldn't read a catalogue photo for any of the brief's products.");
}
