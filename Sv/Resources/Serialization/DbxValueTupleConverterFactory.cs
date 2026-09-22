using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Serialization;

public sealed class DbxValueTupleConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;
        Type genericDef = typeToConvert.GetGenericTypeDefinition();
        return genericDef == typeof(ValueTuple<,>) || genericDef == typeof(ValueTuple<,,>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type genericDef = typeToConvert.GetGenericTypeDefinition();
        Type[] args = typeToConvert.GetGenericArguments();

        if (genericDef == typeof(ValueTuple<,>))
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(DbxValueTuple2Converter<,>).MakeGenericType(args[0], args[1]))!;
        }

        if (genericDef == typeof(ValueTuple<,,>))
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(DbxValueTuple3Converter<,,>).MakeGenericType(args[0], args[1], args[2]))!;
        }

        return null;
    }

    private sealed class DbxValueTuple2Converter<T1, T2> : JsonConverter<ValueTuple<T1, T2>>
    {
        public override (T1, T2) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;
            JsonElement array = root;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$tuple", out JsonElement tupleProp))
            {
                array = tupleProp;
            }

            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < 2)
            {
                return default;
            }

            T1? v1 = array[0].Deserialize<T1>(options);
            T2? v2 = array[1].Deserialize<T2>(options);
            return (v1!, v2!);
        }

        public override void Write(Utf8JsonWriter writer, (T1, T2) value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("$tuple");
            JsonSerializer.Serialize(writer, value.Item1, options);
            JsonSerializer.Serialize(writer, value.Item2, options);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    private sealed class DbxValueTuple3Converter<T1, T2, T3> : JsonConverter<ValueTuple<T1, T2, T3>>
    {
        public override (T1, T2, T3) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;
            JsonElement array = root;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$tuple", out JsonElement tupleProp))
            {
                array = tupleProp;
            }

            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < 3)
            {
                return default;
            }

            T1? v1 = array[0].Deserialize<T1>(options);
            T2? v2 = array[1].Deserialize<T2>(options);
            T3? v3 = array[2].Deserialize<T3>(options);
            return (v1!, v2!, v3!);
        }

        public override void Write(Utf8JsonWriter writer, (T1, T2, T3) value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("$tuple");
            JsonSerializer.Serialize(writer, value.Item1, options);
            JsonSerializer.Serialize(writer, value.Item2, options);
            JsonSerializer.Serialize(writer, value.Item3, options);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
