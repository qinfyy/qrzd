using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public enum LevelStageType
{
    NONE = 0,
    MAIN_STORY = 1,
    EMERGENCY_EVENT = 2,
    DOUBLE_WEEK_BOSS = 3,
    WEEK_EVENT = 4,
    CHALLENGE = 5,
    INFINITE = 6,
    WORLDBOSS = 8,
    INSIDE_WORLD = 9,
    RUN_RUN_RUN = 10,
    BEFALL = 11,
    FACTION_BATTLE = 12,
    HAUNTED_HOUSE = 13,
    DEEP_SEA = 14,
    CARNIVAL = 15,
    PURCHASED = 18,
    CURSE_FOREST = 19,
    GUARD_THERESA = 21,
    MAZE = 22,
    DUNGEON = 23,
    BOSSRUSH = 24,
    NIGHTMARE = 25,
    GENERAL = 26,
    STORYMAZE = 27,
    INSIDE_RIFT = 78,
}

public sealed class AbyssStageData : TableToolsTableBase
{
    public const string TablePath = "Data/AbyssStageData";

    public int GroupID { get; set; }

    public override int GetId() => GroupID;

    public int RewardID { get; set; }

    public int GroupImageID { get; set; }

    public List<int> BossLevelID { get; set; } = [];

    public List<int> LevelImageID { get; set; } = [];
}

public sealed class EquipSkinData : TableToolsTableBase
{
    public const string TablePath = "Data/EquipSkinData";

    public int SkinID { get; set; }

    public List<int> EquipmentID { get; set; } = [];

    public int SkinName { get; set; }

    public int SkinDesc { get; set; }

    public int UnlockItem { get; set; }

    public int Star { get; set; }

    public string DisplayImage { get; set; } = string.Empty;

    public int PartnerID { get; set; }

    public string Figure { get; set; } = string.Empty;

    public float FigureX { get; set; }

    public float FigureY { get; set; }

    public float FigureScale { get; set; }

    public string EquipSkinID { get; set; } = string.Empty;

    public bool InitUnlock { get; set; }

    public string AssetPath { get; set; } = string.Empty;

    public string BatteryAssetPath { get; set; } = string.Empty;

    public string RPGAssetPath { get; set; } = string.Empty;

    public int PartnerShow { get; set; }

    public int Sneak { get; set; }

    public override int GetId() => SkinID;
}

public sealed class RPData : TableToolsTableBase
{
    public const string TablePath = "Data/RPData";

    public int CharacterID { get; set; }

    public int NameText { get; set; }

    public int EquipID { get; set; }

    public string OpenTime { get; set; } = string.Empty;

    public int HideCharacter { get; set; }

    public int LevelChooseID { get; set; }

    public int StageID { get; set; }

    public string Banner { get; set; } = string.Empty;

    public float BannerX { get; set; }

    public float BannerY { get; set; }

    public float BannerScale { get; set; }

    public int MainVisualType { get; set; }

    public int OffsetID { get; set; }

    public int DynamicOffsetID { get; set; }

    public override int GetId() => CharacterID;

    public override void Verification()
    {
        if (CharacterID <= 0 || !RoleDataV2.HasKnownEquipmentMetaId(EquipID))
        {
            throw new InvalidDataException($"RP 角色 {CharacterID} 引用了不存在的装备 {EquipID}");
        }
    }
}

public sealed class RPUpgradeRewardData : TableToolsTableBase
{
    public const string TablePath = "Data/RPUpgradeRewardData";

    public int CharacterID { get; set; }

    public int MissionID { get; set; }

    public int MissionType { get; set; }

    public int MissionProgress { get; set; }

    public List<int> MissionParaList { get; set; } = [];

    public int MissionTitle { get; set; }

    public int MissionContent { get; set; }

    public int RewardType { get; set; }

    public int RewardID { get; set; }

    public static int MakeId(int characterId, int missionId) =>
        checked(characterId * 1000 + missionId);

    public override int GetId() => MakeId(CharacterID, MissionID);

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<RPData>(CharacterID) is null || MissionType is < 1 or > 5 || MissionProgress <= 0)
        {
            throw new InvalidDataException($"RP 养成任务 {CharacterID}:{MissionID} 配置非法");
        }
        if (RewardType == 1 && GameTableCatalog.Instance.GetDataById<RewardItemData>(RewardID) is null ||
            RewardType == 2 && GameTableCatalog.Instance.GetDataById<PartnerPosterData>(RewardID) is null ||
            RewardType is not (1 or 2))
        {
            throw new InvalidDataException($"RP 养成任务 {MissionID} 的奖励非法");
        }
    }
}

public sealed class GeneralStageDataItem
{
    /// <summary>stage 名称（JSON 字典键），加载后由 ResourcesLoader 回填；派生字段，不参与 JSON 反序列化。</summary>
    [JsonIgnore]
    public string Name { get; internal set; } = string.Empty;

    [JsonPropertyName("stageNum")]
    public int StageNum { get; init; }

    [JsonPropertyName("additionalStoryType")]
    public int AdditionalStoryType { get; init; }

    [JsonPropertyName("stageType")]
    [JsonConverter(typeof(LevelStageTypeJsonConverter))]
    public LevelStageType StageType { get; init; }

    [JsonPropertyName("levelTypeArray")]
    public List<int> LevelTypeList { get; init; } = [];

    [JsonPropertyName("levelSettingArray")]
    public List<int> LevelSettingList { get; init; } = [];

    [JsonPropertyName("guideType")]
    public int GuideType { get; init; }

    [JsonPropertyName("scoreRewardType")]
    public int ScoreRewardType { get; init; }

    [JsonPropertyName("scoreText")]
    public int ScoreText { get; init; }

    [JsonPropertyName("rankScoreText")]
    public int RankScoreText { get; init; }

    [JsonPropertyName("stageBgGroup")]
    public int StageBgGroup { get; init; }

    [JsonPropertyName("levelDesignCodeDataArray")]
    public List<string> LevelDesignCodeDataList { get; init; } = [];

    [JsonPropertyName("AlwaysShowLevels")]
    public List<int> AlwaysShowLevelList { get; init; } = [];

    [JsonPropertyName("isGuideButtonOpen")]
    public bool IsGuideButtonOpen { get; init; }

    public string GetName() => Name;

    public int GetStageNum() => StageNum;

    public int GetAdditionalStoryType() => AdditionalStoryType;

    public LevelStageType GetStageType() => StageType;

    public IReadOnlyList<int> GetLevelTypeList() => LevelTypeList;
}

internal sealed class LevelStageTypeJsonConverter : JsonConverter<LevelStageType>
{
    public override LevelStageType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numericValue))
        {
            return (LevelStageType)numericValue;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? value = reader.GetString();
            if (int.TryParse(value, out numericValue))
            {
                return (LevelStageType)numericValue;
            }

            if (Enum.TryParse(value, ignoreCase: false, out LevelStageType namedValue))
            {
                return namedValue;
            }
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return LevelStageType.NONE;
        }

        throw new JsonException($"无法把 {reader.TokenType} 转换为 LevelStageType");
    }

    public override void Write(Utf8JsonWriter writer, LevelStageType value, JsonSerializerOptions options) => writer.WriteNumberValue((int)value);
}
