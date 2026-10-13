using System.Globalization;
using System.Net.ServerSentEvents;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal static class LiveItems
{
    public static SseItem<string> Heartbeat() => new("{}", "heartbeat");

    public static SseItem<string> Reset() => new("{}", "reset");

    public static SseItem<string> From(OutboxEvent row) =>
        new(row.Payload, row.Type) { EventId = row.Id.ToString(CultureInfo.InvariantCulture) };
}
