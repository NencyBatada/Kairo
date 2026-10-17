using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kairo.SharedKernel.Access;
using Microsoft.AspNetCore.Http;

namespace Kairo.Contracts.Ask;

public sealed record AskTarget(string Kind, Guid? Id = null, Guid? CampaignId = null);

public sealed record AskDocument(string Title, string Path, JsonElement Data, string Revision);

public sealed record AskPreviewFile(string Name, string Key, string ContentType);

public sealed record AskChange(JsonElement Before, JsonElement After, JsonElement Payload, string[] Warnings, AskPreviewFile[]? Files = null);

public interface IAskResource
{
    string Kind { get; }
    BrandRoles Editors { get; }
    Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken cancellationToken);
    Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken cancellationToken);
    Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken cancellationToken);
}

public sealed class AskResourceException(string message, int status = 409) : Exception(message)
{
    public int Status { get; } = status;
}

public static class AskJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static T Read<T>(JsonElement element) => element.Deserialize<T>(Options)
        ?? throw new AskResourceException("The change is empty.", 400);

    public static string Revision<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options))));

    public static void Unchanged(string expected, AskDocument? document)
    {
        if (document is null) throw new AskResourceException("This item is no longer available.", 404);
        if (document.Revision != expected) throw new AskResourceException("This item changed since the preview. Ask for a new preview.");
    }

    public static JsonElement Merge(JsonElement before, JsonElement changes, params string[] fields)
    {
        Only(changes, fields);
        var merged = JsonNode.Parse(before.GetRawText())!.AsObject();
        foreach (var property in changes.EnumerateObject()) merged[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        return Element(merged);
    }

    public static void Only(JsonElement changes, params string[] fields)
    {
        if (changes.ValueKind != JsonValueKind.Object || !changes.EnumerateObject().Any())
            throw new AskResourceException("Supply the fields to change.", 400);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in changes.EnumerateObject())
        {
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name))
                throw new AskResourceException($"Chat can't change {property.Name}.", 400);
        }
    }

    public static JsonElement Result(IResult result)
    {
        while (result is INestedHttpResult nested) result = nested.Result;
        if (result is IStatusCodeHttpResult { StatusCode: >= 400 } error)
        {
            var detail = result is IValueHttpResult value ? JsonSerializer.Serialize(value.Value, Options) : "The item is unavailable.";
            throw new AskResourceException(detail, error.StatusCode!.Value);
        }
        return result is IValueHttpResult ok ? Element(ok.Value) : Element(new { });
    }
}
