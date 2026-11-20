using System.Text.Json;

namespace Auction.Contracts.Events;

/// <summary>Serializer JSON thống nhất (camelCase) cho message trên RabbitMQ.</summary>
public static class EventSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize<T>(T @event) => JsonSerializer.SerializeToUtf8Bytes(@event, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> body) => JsonSerializer.Deserialize<T>(body, Options);
}
