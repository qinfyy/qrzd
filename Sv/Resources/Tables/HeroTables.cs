using System.Text.Json.Serialization;
using System.Text.Json;

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

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Additional { get; set; } = [];

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

    /// <summary>
    /// 异界体标志。只有部分角色带该字段，缺失或 0 都视为普通角色。
    /// 异界体星级上限为 5/4，升星与神器消耗走 yjt_fragment 而非普通碎片，
    /// 因此不能套用普通角色的养成规则。
    /// </summary>
    [JsonPropertyName("yijieti")]
    public int Yijieti { get; set; }

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

public sealed class HeroStarSkillData : TableBase
{
    public const string TableFileName = "hero_star_skill.json";

    public int HeroId { get; private set; }
    public int StarLevel { get; private set; }
    public int StarOrder { get; private set; }

    [JsonPropertyName("cumInsightValue")]
    public int InsightBonus { get; set; }

    [JsonPropertyName("cumConstructValue")]
    public int ConstructBonus { get; set; }

    [JsonPropertyName("cumLeadershipValue")]
    public int LeadershipBonus { get; set; }

    [JsonPropertyName("cumSkills")]
    public Dictionary<int, int> Skills { get; set; } = [];

    public override void SetKey(JsonElement key)
    {
        JsonElement tuple = key.GetProperty("$tuple");
        HeroId = tuple[0].GetInt32();
        StarLevel = tuple[1].GetInt32();
        StarOrder = tuple[2].GetInt32();
    }

    public override int GetId() => (HeroId * 100) + (StarLevel * 10) + StarOrder;
}

/// <summary>升星消耗。按 (starLevel, starOrder) 索引，fragment 为升到该阶所需碎片数。</summary>
public sealed class HeroStarData : TableBase
{
    public const string TableFileName = "hero_star.json";

    public int StarLevel { get; private set; }
    public int StarOrder { get; private set; }

    [JsonPropertyName("fragment")]
    public int Fragment { get; set; }

    /// <summary>该阶是否标记为强化阶（1/4），客户端据此决定是否显示强化演出。</summary>
    [JsonPropertyName("isenhance")]
    public int IsEnhance { get; set; }

    public override void SetKey(JsonElement key)
    {
        JsonElement tuple = key.GetProperty("$tuple");
        StarLevel = tuple[0].GetInt32();
        StarOrder = tuple[1].GetInt32();
    }

    public override int GetId() => (StarLevel * 10) + StarOrder;
}

/// <summary>神器等级消耗。按神器等级索引。</summary>
public sealed class ArtifactLevelData : TableBase
{
    public const string TableFileName = "hero_artifact.json";

    public int Level { get; private set; }

    [JsonPropertyName("fragment")]
    public int Fragment { get; set; }

    /// <summary>异界结晶体消耗，与普通碎片分开扣除。</summary>
    [JsonPropertyName("yjt_fragment")]
    public int YjtFragment { get; set; }

    public override void SetKey(JsonElement key) => Level = key.GetInt32();

    public override int GetId() => Level;
}

/// <summary>神器每级提供的属性点总数，用于校验已分配点数是否超限。</summary>
public sealed class ArtifactAttrLevelData : TableBase
{
    public const string TableFileName = "artifact_attr_level.json";

    public int Level { get; private set; }

    [JsonPropertyName("point")]
    public int Point { get; set; }

    public override void SetKey(JsonElement key) => Level = key.GetInt32();

    public override int GetId() => Level;
}

/// <summary>觉醒消耗。按 (heroId, awakeLevel) 索引。</summary>
public sealed class AwakeConsumeData : TableBase
{
    public const string TableFileName = "awake_consume_data.json";

    public int HeroId { get; private set; }
    public int AwakeLevel { get; private set; }

    /// <summary>突破该阶段所需的神器等级。</summary>
    [JsonPropertyName("artifact_level_up")]
    public int ArtifactLevelUp { get; set; }

    [JsonPropertyName("awake_skill_id")]
    public int AwakeSkillId { get; set; }

    [JsonPropertyName("awake_skin")]
    public int AwakeSkin { get; set; }

    /// <summary>首次突破消耗的材料物品 ID。</summary>
    [JsonPropertyName("prior_consume_item")]
    public int PriorConsumeItem { get; set; }

    /// <summary>后续每级消耗的材料物品 ID。</summary>
    [JsonPropertyName("update_consume_item")]
    public int UpdateConsumeItem { get; set; }

    [JsonPropertyName("update_consume_num")]
    public int UpdateConsumeNum { get; set; }

    public override void SetKey(JsonElement key)
    {
        JsonElement tuple = key.GetProperty("$tuple");
        HeroId = tuple[0].GetInt32();
        AwakeLevel = tuple[1].GetInt32();
    }

    public override int GetId() => (HeroId * 100) + AwakeLevel;
}

/// <summary>解放每级提供的累计属性，按等级索引。</summary>
public sealed class LiberateLvData : TableBase
{
    public const string TableFileName = "liberate_lv.json";

    public int Level { get; private set; }

    [JsonPropertyName("_base_max_hp")]
    public int BaseMaxHp { get; set; }

    [JsonPropertyName("_base_phy_att_str")]
    public int BasePhysicalAttack { get; set; }

    [JsonPropertyName("_base_mag_att_str")]
    public int BaseMagicAttack { get; set; }

    [JsonPropertyName("_base_phy_def")]
    public int BasePhysicalDefense { get; set; }

    [JsonPropertyName("_base_mag_def")]
    public int BaseMagicDefense { get; set; }

    public override void SetKey(JsonElement key) => Level = key.GetInt32();

    public override int GetId() => Level;
}

/// <summary>
/// 解放突破阶段。按 (heroId, order) 索引，lvMax 是该阶段可达到的解放等级上限。
/// GM 直设解放阶段时必须按这里截断，否则会写出客户端无法渲染的越界等级。
/// </summary>
public sealed class LiberateTupoData : TableBase
{
    public const string TableFileName = "liberate_tupo.json";

    public int HeroId { get; private set; }
    public int Order { get; private set; }

    /// <summary>该阶段允许达到的解放等级上限。</summary>
    [JsonPropertyName("lv_max")]
    public int LvMax { get; set; }

    /// <summary>进入该阶段所需的等级。</summary>
    [JsonPropertyName("lv_require")]
    public int LvRequire { get; set; }

    public override void SetKey(JsonElement key)
    {
        JsonElement tuple = key.GetProperty("$tuple");
        HeroId = tuple[0].GetInt32();
        Order = tuple[1].GetInt32();
    }

    public override int GetId() => (HeroId * 100) + Order;
}
