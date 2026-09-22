using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Serialization;

public sealed class DbxTupleListConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (typeToConvert == typeof(string) || typeToConvert.IsPrimitive || typeToConvert == typeof(byte[]))
        {
            return false;
        }

        if (typeToConvert.IsArray)
        {
            return typeToConvert.GetArrayRank() == 1;
        }

        if (typeToConvert.IsGenericType)
        {
            Type genericTypeDef = typeToConvert.GetGenericTypeDefinition();
            return genericTypeDef == typeof(List<>)
                || genericTypeDef == typeof(IList<>)
                || genericTypeDef == typeof(IReadOnlyList<>)
                || genericTypeDef == typeof(ICollection<>)
                || genericTypeDef == typeof(IEnumerable<>)
                || genericTypeDef == typeof(HashSet<>)
                || genericTypeDef == typeof(ISet<>)
                || genericTypeDef == typeof(IReadOnlySet<>);
        }

        return false;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert.IsArray)
        {
            Type elementType = typeToConvert.GetElementType()!;
            return (JsonConverter)Activator.CreateInstance(typeof(DbxArrayConverter<>).MakeGenericType(elementType))!;
        }

        Type genericArg = typeToConvert.GetGenericArguments()[0];
        Type genericTypeDef = typeToConvert.GetGenericTypeDefinition();

        if (genericTypeDef == typeof(HashSet<>) || genericTypeDef == typeof(ISet<>) || genericTypeDef == typeof(IReadOnlySet<>))
        {
            return (JsonConverter)Activator.CreateInstance(typeof(DbxHashSetConverter<>).MakeGenericType(genericArg))!;
        }

        if (genericTypeDef == typeof(IReadOnlyList<>))
        {
            return (JsonConverter)Activator.CreateInstance(typeof(DbxIReadOnlyListConverter<>).MakeGenericType(genericArg))!;
        }

        if (genericTypeDef == typeof(IList<>) || genericTypeDef == typeof(ICollection<>) || genericTypeDef == typeof(IEnumerable<>))
        {
            return (JsonConverter)Activator.CreateInstance(typeof(DbxIListConverter<>).MakeGenericType(genericArg))!;
        }

        return (JsonConverter)Activator.CreateInstance(typeof(DbxListConverter<>).MakeGenericType(genericArg))!;
    }

    private static void ReadIntoList<T>(ref Utf8JsonReader reader, List<T> list, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propName = reader.GetString()!;
                    reader.Read();
                    if (propName == "$tuple" && reader.TokenType == JsonTokenType.StartArray)
                    {
                        ReadArrayItems(ref reader, list, options);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            ReadArrayItems(ref reader, list, options);
        }
        else
        {
            throw new JsonException($"无法将 token {reader.TokenType} 转换为列表");
        }
    }

    private static void ReadArrayItems<T>(ref Utf8JsonReader reader, List<T> list, JsonSerializerOptions options)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            T? item = JsonSerializer.Deserialize<T>(ref reader, options);
            if (item is not null)
            {
                list.Add(item);
            }
        }
    }

    private sealed class DbxListConverter<T> : JsonConverter<List<T>>
    {
        public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            List<T> list = [];
            ReadIntoList(ref reader, list, options);
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class DbxIListConverter<T> : JsonConverter<IList<T>>
    {
        public override IList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            List<T> list = [];
            ReadIntoList(ref reader, list, options);
            return list;
        }

        public override void Write(Utf8JsonWriter writer, IList<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class DbxIReadOnlyListConverter<T> : JsonConverter<IReadOnlyList<T>>
    {
        public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            List<T> list = [];
            ReadIntoList(ref reader, list, options);
            return list;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class DbxArrayConverter<T> : JsonConverter<T[]>
    {
        public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            List<T> list = [];
            ReadIntoList(ref reader, list, options);
            return [.. list];
        }

        public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class DbxHashSetConverter<T> : JsonConverter<HashSet<T>>
    {
        public override HashSet<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            List<T> list = [];
            ReadIntoList(ref reader, list, options);
            return [.. list];
        }

        public override void Write(Utf8JsonWriter writer, HashSet<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }
}
