using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public sealed class HeroData : TableBase
{
    public const string TableFileName = "hero.json";

    [JsonPropertyName("protoid")]
    public int ProtoId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = string.Empty;

    [JsonPropertyName("tips_desc")]
    public string TipsDesc { get; set; } = string.Empty;

    [JsonPropertyName("hero_type")]
    public int HeroType { get; set; }

    [JsonPropertyName("attack_type")]
    public int AttackType { get; set; }

    [JsonPropertyName("armor_type")]
    public int ArmorType { get; set; }

    [JsonPropertyName("hero_attack_type")]
    public int HeroAttackType { get; set; }

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = string.Empty;

    [JsonPropertyName("a_skills")]
    public List<int> ActiveSkills { get; set; } = [];

    [JsonPropertyName("p_skill")]
    public int PassiveSkill { get; set; }

    [JsonPropertyName("normal_atk_skill")]
    public int NormalAttackSkill { get; set; }

    [JsonPropertyName("attrs")]
    public Dictionary<string, double> Attrs { get; set; } = [];

    [JsonPropertyName("constructValue")]
    public int ConstructValue { get; set; }

    [JsonPropertyName("insightValue")]
    public int InsightValue { get; set; }

    [JsonPropertyName("leadershipValue")]
    public int LeadershipValue { get; set; }

    [JsonPropertyName("fatigueValue")]
    public int FatigueValue { get; set; } = 100;

    [JsonPropertyName("exchangeNum")]
    public int ExchangeNum { get; set; }

    [JsonPropertyName("sync_cbg")]
    public int SyncCbg { get; set; }

    [JsonPropertyName("open_awake")]
    public int OpenAwake { get; set; }

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    [JsonPropertyName("base_move_speed")]
    public double BaseMoveSpeed { get; set; }

    [JsonPropertyName("base_steering_speed")]
    public double BaseSteeringSpeed { get; set; }

    [JsonPropertyName("pureMission")]
    public int PureMission { get; set; }

    [JsonPropertyName("dailyEndEvent")]
    public int DailyEndEvent { get; set; }

    [JsonPropertyName("good_night")]
    public List<int> GoodNight { get; set; } = [];

    public override int GetId() => ProtoId;

    public override void OnLoad()
    {
        // 预计算或确保默认值
        if (FatigueValue <= 0) FatigueValue = 100;
    }

    public override void Verification()
    {
        if (ProtoId <= 0)
        {
            throw new InvalidDataException($"神器使 ID 非法: {ProtoId}");
        }
    }
}
