using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Serialization;

public sealed class DbxStringJsonConverter : JsonConverter<string>
{
    private static readonly Encoding Gb18030;

    static DbxStringJsonConverter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding enc;
        try
        {
            enc = Encoding.GetEncoding("GB18030");
        }
        catch
        {
            enc = Encoding.GetEncoding(936);
        }
        Gb18030 = enc;
    }

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDecimal().ToString();
        }

        if (reader.TokenType == JsonTokenType.True)
        {
            return "true";
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            return "false";
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("$binary", out JsonElement binProp) && binProp.ValueKind == JsonValueKind.String)
            {
                string hex = binProp.GetString()!;
                byte[] bytes = Convert.FromHexString(hex);
                try
                {
                    // 优先尝试严格 UTF-8，若有替换字符或错误则回退至 GB18030
                    string utf8 = new UTF8Encoding(false, true).GetString(bytes);
                    return utf8;
                }
                catch
                {
                    return Gb18030.GetString(bytes);
                }
            }

            return doc.RootElement.GetRawText();
        }

        throw new JsonException($"无法将 token {reader.TokenType} 转换为字符串");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
