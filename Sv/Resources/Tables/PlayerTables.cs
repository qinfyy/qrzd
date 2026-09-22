using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public sealed class PlayerGeneralLogicDataV2 : TableBase
{
    public const string TablePath = "Data/PlayerGeneralLogicData";

    public int Level { get; set; }

    public override int GetId() => Level;

    public int Exp { get; set; }

    public int Hp { get; set; }

    public int Stamina { get; set; }

    public int TotalCost { get; set; }

    public int NumFriends { get; set; }

    public int HardLevel { get; set; }

    public int PvpCost { get; set; }
}

public sealed class TutorialGeneralLogicData
{
    public const string TablePath = "Data/TutorialEquipments";

    [JsonPropertyName("FriendLV")]
    public int TUTORIAL_FRIEND_LV { get; init; }

    [JsonPropertyName("FriendVIPLV")]
    public int TUTORIAL_FRIEND_VIPLV { get; init; }

    [JsonPropertyName("FriendEquip")]
    public int[] TUTORIAL_FRIEND_EQUIMENT_MID { get; init; } = [];

    [JsonPropertyName("FriendEquipLV")]
    public int[] TUTORIAL_FRIEND_EQUIMENT_LV { get; init; } = [];

    [JsonPropertyName("FriendIconID")]
    public int TUTORIAL_FRIEND_ICONID { get; init; }

    [JsonPropertyName("FriendPetID")]
    public int TUTORIAL_FRIEND_PET_ID { get; init; }

    [JsonPropertyName("FriendPetLV")]
    public int TUTORIAL_FRIEND_PET_LV { get; init; }

    [JsonPropertyName("PlayerEquip")]
    public int[] TUTORIAL_PLAYER_EQUIMENT_MID { get; init; } = [];

    [JsonPropertyName("PlayerEquipLV")]
    public int[] TUTORIAL_PLAYER_EQUIMENT_LV { get; init; } = [];

    [JsonPropertyName("PlayerPetID")]
    public int TUTORIAL_PLAYER_PET_ID { get; init; }

    [JsonPropertyName("PlayerPetLV")]
    public int TUTORIAL_PLAYER_PET_LV { get; init; }
}

/// <summary>
/// 客户端强制引导步骤。服务端只读取生成初始完成游标所需的字段。
/// </summary>
public sealed class TutorialData : TableBase
{
    public const string TablePath = "Data/forceguidedata/TutorialData";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int ForceTutorialType { get; set; }
}

/// <summary>
/// 客户端弱引导配置。服务端只读取初始化弱引导位图所需的字段。
/// </summary>
public sealed class WeakGuideData : TableBase
{
    public const string TablePath = "Data/WeakGuideData";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int FlagID { get; set; }
}

/// <summary>
/// 普通弱引导页面配置。客户端按同一 GuideID 在资源中的首次出现记录 season。
/// </summary>
public sealed class GuideData : TableToolsTableBase
{
    public const string TablePath = "Data/GuideData";

    public int GuideID { get; set; }

    public int ImageID { get; set; }

    public string WebUrl { get; set; } = string.Empty;

    public int TextID { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// 客户端功能模块开放条件。当前版本只使用玩家等级和首次通关两种条件，
/// 系统名和参数由客户端映射为具体 UI 功能模块。
/// </summary>
public sealed class NewPlayerDetentionV2 : TableToolsTableBase
{
    public const string TablePath = "Data/NewPlayerDetentionV2";

    /// <summary>开放条件类型：玩家等级达到 UnlockParam。</summary>
    public const int PlayerLevelCondition = 1;
    /// <summary>开放条件类型：首次通关 UnlockParam 指定关卡。</summary>
    public const int LevelFirstPassCondition = 2;

    public string Systemname { get; set; } = string.Empty;

    public string SystemParam1 { get; set; } = string.Empty;

    public string SystemParam2 { get; set; } = string.Empty;

    public int UnlockConditionType { get; set; }

    public string UnlockParam { get; set; } = string.Empty;

    public int UnlockTEXTMAP { get; set; }

    public int Jumptype { get; set; }

    public string Jumpparam { get; set; } = string.Empty;

    public int Ifshow { get; set; }

    public int SystemnameTEXTMAP { get; set; }

    public int DelayTime { get; set; }

    public override void Verification()
    {
        if (string.IsNullOrWhiteSpace(Systemname) || !int.TryParse(UnlockParam, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parameter) || parameter <= 0)
        {
            throw new InvalidDataException($"NewPlayerDetentionV2 功能 {Systemname} 的开放参数非法: {UnlockParam}");
        }

        switch (UnlockConditionType)
        {
            case PlayerLevelCondition:
                if (GameTableCatalog.Instance.GetDataById<PlayerGeneralLogicDataV2>(parameter) is null)
                {
                    int maximumPlayerLevel = GameTableCatalog.Instance.GetAllData<PlayerGeneralLogicDataV2>().Max(value => value.Level);
                    if (parameter <= maximumPlayerLevel)
                    {
                        throw new InvalidDataException($"NewPlayerDetentionV2 功能 {Systemname} 引用了不存在的玩家等级 {parameter}");
                    }
                }
                break;
            case LevelFirstPassCondition:
                bool teachingLevel = parameter is >= LevelMetaV2.FirstNewPlayerTeachingLevelId and <= LevelMetaV2.LastNewPlayerTeachingLevelId;
                if (!teachingLevel && GameTableCatalog.Instance.GetDataById<LevelMetaV2>(parameter)?.IsOrdinaryStory != true)
                {
                    throw new InvalidDataException($"NewPlayerDetentionV2 功能 {Systemname} 引用了不存在的首次通关关卡 {parameter}");
                }
                break;
            default:
                throw new InvalidDataException($"NewPlayerDetentionV2 功能 {Systemname} 使用了尚未支持的开放条件 {UnlockConditionType}");
        }
    }
}

public sealed class RoleData : TableBase
{
    public const string TablePath = "Data/RoleData";

    public string Name { get; set; } = string.Empty;

    public int ID { get; set; }

    public override int GetId() => ID;

    public int ROLE_NAME { get; set; }

    public int AGE { get; set; }

    public int BIRTH { get; set; }

    public int DESP { get; set; }

    public int PROPS { get; set; }

    public int ICON { get; set; }

    public string FigureID { get; set; } = string.Empty;

    public int PosterID { get; set; }

    /// <summary>该角色是否有 RoleV2（角色类型 2）的养成资源（特训轨迹/限解）。</summary>
    public bool IsRoleV2() =>
        GameTableCatalog.Instance.GetDataById<RoleDataV2>(ID) is not null;

    /// <summary>本地化名称：TextMap 角色名，缺省回退资源内名称或 MetaId。</summary>
    public string GetLocalizedName()
    {
        if (ROLE_NAME > 0 && GameTableCatalog.Instance.GetDataById<TextMapData>(ROLE_NAME)?.CONTENT is { Length: > 0 } localized)
        {
            return localized;
        }
        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }
        return ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public override void Verification()
    {
        if (ID > 0 && (!TextMapData.Exists(ROLE_NAME) || !TextMapData.Exists(DESP)))
        {
            throw new InvalidDataException($"装备 5:{ID} 存在无效的 TextMap 标题或描述引用");
        }
    }
}

/// <summary>
/// 角色语音配置。字段名保持 13.2.8_341 客户端 <c>RoleVoiceData</c> 定义。
/// </summary>
public sealed class RoleVoiceData : TableBase
{
    public const string TablePath = "Data/RoleVoiceData";

    public int voiceID { get; set; }

    public override int GetId() => voiceID;

    public string voicePath { get; set; } = string.Empty;

    public int voiceTitle { get; set; }

    public int voiceTxt { get; set; }

    public int isOpenVoice { get; set; }

    public int isNew { get; set; }

    public int voiceType { get; set; }

    public int cVID { get; set; }

    public override void Verification()
    {
        if (voiceID <= 0 || string.IsNullOrWhiteSpace(voicePath) || isOpenVoice is not (0 or 1) || isNew is not (0 or 1) || voiceType is not (1 or 2) || cVID <= 0)
        {
            throw new InvalidDataException($"角色语音 {voiceID} 配置非法");
        }

        IReadOnlyList<RoleVoiceData> voices = GameTableCatalog.Instance.GetAllData<RoleVoiceData>();
        if (ReferenceEquals(voices[0], this) && voices.All(value => value.isNew != 1))
        {
            throw new InvalidDataException("RoleVoiceData 缺少 IsNew=1 的角色语音");
        }
    }
}

public sealed class PlayerSkinData : TableToolsTableBase
{
    public const string TablePath = "Data/PlayerSkinData";

    /// <summary>皮肤表不是按行序索引：客户端与 GM 都按 SkinID 定位皮肤。</summary>
    public override int GetId() => SkinID;

    public int SkinID { get; set; }

    public string Character { get; set; } = string.Empty;

    public int Type { get; set; }

    public int SkinName { get; set; }

    public int SkinDesp { get; set; }

    public bool SetDefault { get; set; }

    public int InitUnlock { get; set; }

    public int DisplayImage { get; set; }

    public int IsShow { get; set; }

    public int Star { get; set; }

    public int OrderID { get; set; }

    public IReadOnlyList<int> GetCharacters() => Character
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => int.TryParse(value, out int roleId) ? roleId : 0)
        .Where(value => value > 0)
        .Distinct()
        .Order()
        .ToArray();

    public int GetFirstCharacter() =>
        GetCharacters().FirstOrDefault();

    public static IReadOnlyList<PlayerSkinData> GetForRole(int roleId) =>
        GameTableCatalog.Instance.GetAllData<PlayerSkinData>()
            .Where(value => value.GetCharacters().Contains(roleId))
            .OrderBy(value => value.OrderID)
            .ThenBy(value => value.SkinID)
            .ToArray();

    public override void Verification()
    {
        if (SkinID <= 0)
        {
            throw new InvalidDataException($"角色皮肤 ID 非法 {SkinID}");
        }
        if (GetCharacters().Count == 0)
        {
            throw new InvalidDataException($"角色皮肤 {SkinID} 缺少适用角色");
        }
    }
}
