using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kairo.Contracts.Approvals;

public interface IApprovalLocks
{
    Task<ApprovalEvidence> ApproveAsync(Guid roomId, string gate, Guid approverId, string snapshot, CancellationToken cancellationToken);

    Task<bool> MatchesAsync(Guid roomId, string gate, string snapshot, CancellationToken cancellationToken);

    Task InvalidateAsync(Guid roomId, string[] gates, CancellationToken cancellationToken);
}

public sealed record ApprovalEvidence(long Id, Guid RoomId, string Gate, Guid ApproverId, DateTimeOffset ApprovedAt, string Hash, string Snapshot);

public static class ApprovalHash
{
    public static string Of(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, document.RootElement);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                Write(writer, item);
            }

            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}
