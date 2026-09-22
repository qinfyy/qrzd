using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Serialization;

public sealed class DbxByteArrayJsonConverter : JsonConverter<byte[]>
{
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString()!;
            try
            {
                return Convert.FromHexString(str);
            }
            catch
            {
                return Convert.FromBase64String(str);
            }
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("$binary", out JsonElement binProp) && binProp.ValueKind == JsonValueKind.String)
            {
                return Convert.FromHexString(binProp.GetString()!);
            }
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<byte> bytes = [];
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;
                bytes.Add(reader.GetByte());
            }
            return [.. bytes];
        }

        throw new JsonException($"无法将 token {reader.TokenType} 转换为 byte[]");
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Convert.ToHexString(value));
}
