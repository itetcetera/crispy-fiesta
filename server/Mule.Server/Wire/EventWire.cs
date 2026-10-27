using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mule.Engine.Flow;
using Mule.Engine.Rules;

namespace Mule.Server.Wire;

/// <summary>Game events as JSON for the browser: the event's fields plus "t", its type name.</summary>
public static class EventWire
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.Converters.Add(new ByteArrayAsNumbers());
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static JsonObject ToJson(GameEvent e)
    {
        var node = JsonSerializer.SerializeToNode(e, e.GetType(), Options) as JsonObject ?? new JsonObject();
        string name = e.GetType().Name;
        node["t"] = name.EndsWith("Event") ? name[..^5] : name;
        // The text the original scrolls for a player event.
        if (e is PlayerEventShownEvent p) node["text"] = PlayerEvents.Text(p.Result);
        return node;
    }

    /// <summary>Byte arrays as plain number arrays (not base64), which the client reads as they are.</summary>
    sealed class ByteArrayAsNumbers : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var list = new List<byte>();
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) list.Add(reader.GetByte());
            return list.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var b in value) writer.WriteNumberValue(b);
            writer.WriteEndArray();
        }
    }
}
