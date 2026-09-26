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

    // Client com.const.ItemType.CATEGORY_*; virtual rewards share COMMON but are not inventory entities.
    public string InventoryCategory => Type switch
    {
        5 => "treasure",
        2 => "task",
        10 or 21 or 24 => "gift",
        20 => "furniture",
        13 => "xinwu",
        1 or 3 or 4 or 6 or 7 or 8 or 9 or 11 or 14 or 15 or 16 or 17 or 18 or 19 or 22 or 23 => "common",
        _ => "",
    };

    public bool IsStoredInInventory => Type != 8 && InventoryCategory.Length > 0;

    public string TypeName => Type switch
    {
        1 => "COMMON", 2 => "TASK", 3 => "HERO_FRAGMENT", 4 => "HERO_CRYSTAL", 5 => "HERO_TREASURE", 6 => "REFRESH", 7 => "BREAK", 8 => "VIRTUAL",
        9 => "HERO_ITEM", 10 => "GIFT", 11 => "DYE", 13 => "XINWU", 14 => "XUNZHANG", 15 => "TREASUREEXPITEM", 16 => "SKIN_TEXTURE",
        17 => "MEMORY_CRYSTAL", 18 => "HERO_HOME_GIFT", 19 => "SPE_LIBERATE_ITEM", 20 => "FURNITURE", 21 => "CHOOSE_HERO_FRAGMENT",
        22 => "YIJIETI_HERO_FRAGMENT", 23 => "YIJIETI_HERO_MEMORY_CRYSTAL", 24 => "XKJL_PET_CHANGE", 25 => "ARITIFACT_INC_FRAGMENT",
        26 => "ARITIFACT_INC_CRYSTAL", _ => "UNKNOWN",
    };

    public override void Verification()
    {
        if (Id <= 0)
        {
            throw new InvalidDataException($"道具 ID 非法: {Id}");
        }
    }
}
