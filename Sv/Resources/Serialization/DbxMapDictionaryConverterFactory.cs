using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Serialization;

public sealed class DbxMapDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;

        Type genericTypeDef = typeToConvert.GetGenericTypeDefinition();
        return genericTypeDef == typeof(Dictionary<,>)
            || genericTypeDef == typeof(IDictionary<,>)
            || genericTypeDef == typeof(IReadOnlyDictionary<,>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type[] genericArgs = typeToConvert.GetGenericArguments();
        Type keyType = genericArgs[0];
        Type valueType = genericArgs[1];
        Type genericTypeDef = typeToConvert.GetGenericTypeDefinition();

        if (genericTypeDef == typeof(IReadOnlyDictionary<,>))
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(DbxIReadOnlyDictionaryConverter<,>).MakeGenericType(keyType, valueType))!;
        }

        if (genericTypeDef == typeof(IDictionary<,>))
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(DbxIDictionaryConverter<,>).MakeGenericType(keyType, valueType))!;
        }

        return (JsonConverter)Activator.CreateInstance(
            typeof(DbxDictionaryConverter<,>).MakeGenericType(keyType, valueType))!;
    }

    private static void ReadIntoDictionary<TKey, TValue>(ref Utf8JsonReader reader, Dictionary<TKey, TValue> dict, JsonSerializerOptions options) where TKey : notnull
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("$map", out JsonElement mapElement) && mapElement.ValueKind == JsonValueKind.Array)
            {
                ReadMapArray(mapElement, dict, options);
                return;
            }

            // 普通对象键值对解析
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                TKey key = ConvertKey<TKey>(prop.Name, options);
                TValue? val = prop.Value.Deserialize<TValue>(options);
                if (val is not null)
                {
                    dict[key] = val;
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            ReadMapArray(doc.RootElement, dict, options);
        }
        else
        {
            throw new JsonException($"无法将 token {reader.TokenType} 转换为字典");
        }
    }

    private static void ReadMapArray<TKey, TValue>(JsonElement arrayElement, Dictionary<TKey, TValue> dict, JsonSerializerOptions options) where TKey : notnull
    {
        foreach (JsonElement pair in arrayElement.EnumerateArray())
        {
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() >= 2)
            {
                TKey? k = pair[0].Deserialize<TKey>(options);
                TValue? v = pair[1].Deserialize<TValue>(options);
                if (k is not null && v is not null)
                {
                    dict[k] = v;
                }
            }
        }
    }

    private static TKey ConvertKey<TKey>(string name, JsonSerializerOptions options) where TKey : notnull
    {
        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)name;
        }

        if (typeof(TKey) == typeof(int))
        {
            return (TKey)(object)int.Parse(name);
        }

        if (typeof(TKey) == typeof(long))
        {
            return (TKey)(object)long.Parse(name);
        }

        TypeConverter converter = TypeDescriptor.GetConverter(typeof(TKey));
        if (converter.CanConvertFrom(typeof(string)))
        {
            object? converted = converter.ConvertFromInvariantString(name);
            if (converted is not null)
            {
                return (TKey)converted;
            }
        }

        return JsonSerializer.Deserialize<TKey>($"\"{name}\"", options)!;
    }

    private sealed class DbxDictionaryConverter<TKey, TValue> : JsonConverter<Dictionary<TKey, TValue>> where TKey : notnull
    {
        public override Dictionary<TKey, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            Dictionary<TKey, TValue> dict = [];
            ReadIntoDictionary(ref reader, dict, options);
            return dict;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<TKey, TValue> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class DbxIDictionaryConverter<TKey, TValue> : JsonConverter<IDictionary<TKey, TValue>> where TKey : notnull
    {
        public override IDictionary<TKey, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            Dictionary<TKey, TValue> dict = [];
            ReadIntoDictionary(ref reader, dict, options);
            return dict;
        }

        public override void Write(Utf8JsonWriter writer, IDictionary<TKey, TValue> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class DbxIReadOnlyDictionaryConverter<TKey, TValue> : JsonConverter<IReadOnlyDictionary<TKey, TValue>> where TKey : notnull
    {
        public override IReadOnlyDictionary<TKey, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            Dictionary<TKey, TValue> dict = [];
            ReadIntoDictionary(ref reader, dict, options);
            return dict;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }
}
