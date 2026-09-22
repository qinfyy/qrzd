using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public sealed class DbxTableFile<T> where T : TableBase
{
    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    [JsonPropertyName("rows")]
    public List<DbxTableRow<T>> Rows { get; set; } = [];
}

public sealed class DbxTableRow<T> where T : TableBase
{
    [JsonPropertyName("key")]
    public JsonElement Key { get; set; }

    [JsonPropertyName("value")]
    public T Value { get; set; } = default!;
}
