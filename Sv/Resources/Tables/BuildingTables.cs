using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public sealed class BuildingData : TableBase
{
    public const string TableFileName = "building.json";

    [JsonPropertyName("protoid")]
    public int ProtoId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = string.Empty;

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    [JsonPropertyName("range")]
    public int Range { get; set; }

    [JsonPropertyName("is_protected")]
    public bool IsProtected { get; set; }

    [JsonPropertyName("ai_type")]
    public int AiType { get; set; }

    [JsonPropertyName("base_att_speed")]
    public double BaseAttSpeed { get; set; }

    [JsonPropertyName("normal_atk_skill")]
    public int NormalAtkSkill { get; set; }

    [JsonPropertyName("sight_range")]
    public int SightRange { get; set; }

    [JsonPropertyName("radius")]
    public double Radius { get; set; }

    [JsonPropertyName("attrs")]
    public Dictionary<string, double> Attrs { get; set; } = [];

    [JsonPropertyName("obs")]
    public List<List<int>> Obstacles { get; set; } = [];

    [JsonPropertyName("boss_hp_bar_num")]
    public int BossHpBarNum { get; set; }

    [JsonPropertyName("icon")]
    public int Icon { get; set; }

    public override int GetId() => ProtoId;
}
