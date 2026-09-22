using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public sealed class ItemData : TableBase
{
    public const string TableFileName = "item_data.json";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("subtype")]
    public int SubType { get; set; }

    [JsonPropertyName("virtual_sub_type")]
    public int VirtualSubType { get; set; }

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("star")]
    public int Star { get; set; }

    [JsonPropertyName("wrap")]
    public int Wrap { get; set; } = 1;

    [JsonPropertyName("release")]
    public bool Release { get; set; }

    [JsonPropertyName("sort_level")]
    public int SortLevel { get; set; }

    [JsonPropertyName("tradeType")]
    public int TradeType { get; set; }

    [JsonPropertyName("heroid")]
    public int HeroId { get; set; }

    [JsonPropertyName("piece")]
    public int Piece { get; set; }

    [JsonPropertyName("use_type")]
    public int UseType { get; set; }

    [JsonPropertyName("ui_class")]
    public int UiClass { get; set; }

    [JsonPropertyName("ui_subclass")]
    public int UiSubClass { get; set; }

    [JsonPropertyName("clear_inc_week")]
    public int ClearIncWeek { get; set; }

    [JsonPropertyName("crystalLevel")]
    public int CrystalLevel { get; set; }

    public override int GetId() => Id;

    public bool IsFragment => Piece > 0 && HeroId > 0;

    public bool IsWeekReset => ClearIncWeek > 0;

    public override void Verification()
    {
        if (Id <= 0)
        {
            throw new InvalidDataException($"道具 ID 非法: {Id}");
        }
    }
}
