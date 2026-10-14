using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kairo.Modules.BrandKit.Kit;

internal static class KitJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(KitContent content) => JsonSerializer.Serialize(content, Options);

    public static KitContent Read(string json) => JsonSerializer.Deserialize<KitContent>(json, Options) ?? new KitContent();

    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string Name<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
