using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Serialization;

public sealed class DbxFloatJsonConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetSingle();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString()!;
            return ParseFloat(str);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("$float", out JsonElement floatProp))
            {
                if (floatProp.ValueKind == JsonValueKind.Number)
                {
                    return floatProp.GetSingle();
                }
                if (floatProp.ValueKind == JsonValueKind.String)
                {
                    return ParseFloat(floatProp.GetString()!);
                }
            }
        }

        throw new JsonException($"无法将 token {reader.TokenType} 转换为 float");
    }

    private static float ParseFloat(string str)
    {
        if (string.Equals(str, "nan", StringComparison.OrdinalIgnoreCase)) return float.NaN;
        if (string.Equals(str, "inf", StringComparison.OrdinalIgnoreCase)) return float.PositiveInfinity;
        if (string.Equals(str, "+inf", StringComparison.OrdinalIgnoreCase)) return float.PositiveInfinity;
        if (string.Equals(str, "-inf", StringComparison.OrdinalIgnoreCase)) return float.NegativeInfinity;
        return float.Parse(str, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            writer.WriteStartObject();
            writer.WriteString("$float", float.IsNaN(value) ? "nan" : (float.IsPositiveInfinity(value) ? "inf" : "-inf"));
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }
}

public sealed class DbxDoubleJsonConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString()!;
            return ParseDouble(str);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("$float", out JsonElement floatProp))
            {
                if (floatProp.ValueKind == JsonValueKind.Number)
                {
                    return floatProp.GetDouble();
                }
                if (floatProp.ValueKind == JsonValueKind.String)
                {
                    return ParseDouble(floatProp.GetString()!);
                }
            }
        }

        throw new JsonException($"无法将 token {reader.TokenType} 转换为 double");
    }

    private static double ParseDouble(string str)
    {
        if (string.Equals(str, "nan", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        if (string.Equals(str, "inf", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
        if (string.Equals(str, "+inf", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
        if (string.Equals(str, "-inf", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;
        return double.Parse(str, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            writer.WriteStartObject();
            writer.WriteString("$float", double.IsNaN(value) ? "nan" : (double.IsPositiveInfinity(value) ? "inf" : "-inf"));
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }
}
