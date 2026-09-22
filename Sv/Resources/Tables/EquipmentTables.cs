using System.Globalization;
using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

/// <summary>
/// 武器、服装与徽章三张同构装备表的公共基类：承载共享 TSV 列与行级逻辑
/// （发放判定、技能提取、升星材料、掉落许可、本地化名）及共享资源校验。
/// </summary>
public abstract class EquipmentTableBase : TableBase
{
    /// <summary>装备类型（1=武器 2=服装 3=徽章）。</summary>
    public abstract byte TypeId { get; }

    public string Name { get; set; } = string.Empty;

    public int ID { get; set; }

    public override int GetId() => ID;

    public int Rarity { get; set; }

    public int Cost { get; set; }

    public int MaxLv { get; set; }

    public int ExpType { get; set; }

    public double ExpProvideBase { get; set; }

    public double ExpProvideAdd { get; set; }

    public bool AllowClientDrop { get; set; }

    public int DisplayTitle { get; set; }

    public int DisplayDescription { get; set; }

    public int NumProps { get; set; }

    [Name("Prop1id")]
    public int Prop1Id { get; set; }

    [Name("Prop2id")]
    public int Prop2Id { get; set; }

    [Name("Prop3id")]
    public int Prop3Id { get; set; }

    [Name("Prop4id")]
    public int Prop4Id { get; set; }

    [Name("Prop5id")]
    public int Prop5Id { get; set; }

    [Name("Prop6id")]
    public int Prop6Id { get; set; }

    [Name("Prop7id")]
    public int Prop7Id { get; set; }

    public string StarUpMeta { get; set; } = string.Empty;

    [Name("groupid")]
    public int GroupId { get; set; }

    public int PersonateOn { get; set; }

    public int MaxIntimacy { get; set; }

    public int PersonatedMaxIntimacy { get; set; }

    /// <summary>可发放装备（Rarity 与 MaxLv 均为正）。</summary>
    public bool CanGrant => ID > 0 && Rarity > 0 && MaxLv > 0;

    public bool PersonateEnabled => PersonateOn != 0;

    private IReadOnlyList<int>? _starUpMaterialIds;

    /// <summary>升星材料 MetaId 列表（StarUpMeta 分号分隔），OnLoad 后缓存。</summary>
    [Ignore]
    public IReadOnlyList<int> StarUpMaterialIds =>
        _starUpMaterialIds ??= StarUpMeta
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => int.TryParse(item, out int parsed) ? parsed : 0)
            .Where(item => item > 0)
            .Distinct()
            .ToArray();

    public override void OnLoad()
    {
        _ = StarUpMaterialIds;
    }

    public int[] GetSkillIds() =>
        new[] { Prop1Id, Prop2Id, Prop3Id, Prop4Id, Prop5Id, Prop6Id, Prop7Id }
            .Take(Math.Clamp(NumProps, 0, 7))
            .Where(value => value > 0)
            .ToArray();

    /// <summary>本地化名称：优先 TextMap 标题，其次资源内名称，最后 MetaId。</summary>
    public string GetLocalizedName()
    {
        if (DisplayTitle > 0 && GameTableCatalog.Instance.GetDataById<TextMapData>(DisplayTitle)?.CONTENT is { Length: > 0 } localized)
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
        if (ID <= 0)
        {
            return;
        }

        if (!TextMapData.Exists(DisplayTitle) || !TextMapData.Exists(DisplayDescription))
        {
            throw new InvalidDataException($"装备 {ID} 存在无效的 TextMap 标题或描述引用");
        }

        // 可发放装备（Rarity 与 MaxLv 均为正）必须有合法经验类型、等级曲线与属性引用。
        if (!CanGrant)
        {
            return;
        }

        if (ExpType is < 1 or > 7 || Enumerable.Range(1, MaxLv).Any(level => GameTableCatalog.Instance.GetDataById<PowerupExpTypesData>(level) is null))
        {
            throw new InvalidDataException($"装备 {ID} 的 ExpType 或等级曲线配置非法");
        }

        foreach (int skillId in GetSkillIds())
        {
            if (GameTableCatalog.Instance.GetDataById<SpecialAttributeDataV2>(skillId) is null)
            {
                throw new InvalidDataException($"装备 {ID} 引用了不存在的属性 {skillId}");
            }
        }
    }

    /// <summary>武器/服装/徽章表按装备类型与 MetaId 的联合查找；不存在时返回 null。表类与领域 Logic 共用。</summary>
    public static EquipmentTableBase? Find(byte typeId, int metaId) => typeId switch
    {
        1 => GameTableCatalog.Instance.GetDataById<WeaponDataV3>(metaId),
        2 => GameTableCatalog.Instance.GetDataById<CostumeDataV2>(metaId),
        3 => GameTableCatalog.Instance.GetDataById<PassiveSkillDataV3>(metaId),
        _ => null,
    };

    /// <summary>
    /// 装备类型的线缆名，与客户端 EquipmentGeneralLogic 的类型判定一一对应：
    /// 1 weapon 武器、2 costume 服装、3 badge 徽章、5 role 角色、6 emblem 萌章、9 petchip 使魔碎片。
    /// 未列举的类型返回空串，由调用方决定报错方式。
    /// </summary>
    public static string GetTypeName(byte typeId) => typeId switch
    {
        1 => "weapon",
        2 => "costume",
        3 => "badge",
        5 => "role",
        6 => "emblem",
        9 => "petchip",
        _ => string.Empty,
    };

    /// <summary>
    /// 萌章（EmblemData，类型 6）的 MetaId 区间，对齐客户端 EquipmentGeneralLogic.IsEmblem 与
    /// LevelBattlePackets.IsCanOwnOnlyOne 的 5001--5999 判定。
    /// EmblemData 中小于该区间的 1--18 是学生证空位与 VIP 位，客户端从不把它们当作持有物，
    /// 且与武器 MetaId 1--18 重号，因此必须排除，否则一个 ID 会同时命中武器与萌章两型。
    /// </summary>
    private const int EmblemMetaIdMin = 5001;

    private const int EmblemMetaIdMax = 5999;

    /// <summary>使魔碎片（PetChipData，类型 9）的 MetaId 区间，对齐客户端 EquipmentGeneralLogic.IsPetChip。</summary>
    private const int PetChipMetaIdMin = 8001;

    private const int PetChipMetaIdMax = 8999;

    /// <summary>装备（任意类型 1-9）在对应资源表中存在。</summary>
    public static bool Exists(byte typeId, int metaId) => typeId switch
    {
        1 => GameTableCatalog.Instance.GetDataById<WeaponDataV3>(metaId) is not null,
        2 => GameTableCatalog.Instance.GetDataById<CostumeDataV2>(metaId) is not null,
        3 => GameTableCatalog.Instance.GetDataById<PassiveSkillDataV3>(metaId) is not null,
        5 => GameTableCatalog.Instance.GetDataById<RoleData>(metaId) is not null,
        6 => metaId is >= EmblemMetaIdMin and <= EmblemMetaIdMax && GameTableCatalog.Instance.GetDataById<EmblemData>(metaId) is { IsEquipment: true },
        9 => metaId is >= PetChipMetaIdMin and <= PetChipMetaIdMax && GameTableCatalog.Instance.GetDataById<PetChipData>(metaId) is not null,
        _ => false,
    };

    /// <summary>装备可发放：武器/服装/徽章要求 Rarity 与 MaxLv 为正，角色/萌章/使魔碎片存在即可。</summary>
    public static bool IsGrantable(byte typeId, int metaId) => typeId switch
    {
        1 => GameTableCatalog.Instance.GetDataById<WeaponDataV3>(metaId)?.CanGrant == true,
        2 => GameTableCatalog.Instance.GetDataById<CostumeDataV2>(metaId)?.CanGrant == true,
        3 => GameTableCatalog.Instance.GetDataById<PassiveSkillDataV3>(metaId)?.CanGrant == true,
        5 => GameTableCatalog.Instance.GetDataById<RoleData>(metaId) is not null,
        6 => metaId is >= EmblemMetaIdMin and <= EmblemMetaIdMax && GameTableCatalog.Instance.GetDataById<EmblemData>(metaId) is { IsEquipment: true },
        9 => metaId is >= PetChipMetaIdMin and <= PetChipMetaIdMax && GameTableCatalog.Instance.GetDataById<PetChipData>(metaId) is not null,
        _ => false,
    };

    /// <summary>装备的属性（技能）ID 列表；仅武器/服装/徽章携带，最多 7 条。</summary>
    public static IReadOnlyList<int> GetSkillIds(byte typeId, int metaId) =>
        Find(typeId, metaId)?.GetSkillIds().Where(value => value != 0).Take(7).ToArray() ?? [];

    /// <summary>
    /// 按 MetaId 反查该 ID 能作为哪几种装备类型发放，按类型升序。
    /// 六类装备的 MetaId 区间互不重叠（见各类型区间的常量），因此正常资源下至多命中一型；
    /// 真的命中多类说明资源越界，由调用方显式指定类型或报错。
    /// </summary>
    public static IReadOnlyList<byte> FindGrantableTypes(int metaId) =>
        metaId > 0 ? [.. GrantableTypes.Where(typeId => IsGrantable(typeId, metaId))] : [];

    /// <summary>按 ID 自动分流时参与判定的装备类型全集。</summary>
    private static readonly byte[] GrantableTypes = [1, 2, 3, 5, 6, 9];

    /// <summary>
    /// 全部可发放装备的 (TypeId, MetaId)，按类型与 MetaId 升序；发放目录（give/GM/商店）以此为全集。
    /// 覆盖武器、服装、徽章、角色、萌章与使魔碎片六类，与 <see cref="IsGrantable"/> 的判定口径一致。
    /// </summary>
    public static IReadOnlyList<(byte TypeId, int MetaId)> GetGrantableIds()
    {
        List<(byte TypeId, int MetaId)> values = [];
        values.AddRange(GameTableCatalog.Instance.GetAllData<WeaponDataV3>()
            .Where(value => value.CanGrant).Select(value => ((byte)1, value.ID)));
        values.AddRange(GameTableCatalog.Instance.GetAllData<CostumeDataV2>()
            .Where(value => value.CanGrant).Select(value => ((byte)2, value.ID)));
        values.AddRange(GameTableCatalog.Instance.GetAllData<PassiveSkillDataV3>()
            .Where(value => value.CanGrant).Select(value => ((byte)3, value.ID)));
        values.AddRange(GameTableCatalog.Instance.GetAllData<RoleData>()
            .Where(value => value.ID > 0).Select(value => ((byte)5, value.ID)));
        values.AddRange(GameTableCatalog.Instance.GetAllData<EmblemData>()
            .Where(value => IsGrantable(6, value.ID)).Select(value => ((byte)6, value.ID)));
        values.AddRange(GameTableCatalog.Instance.GetAllData<PetChipData>()
            .Where(value => IsGrantable(9, value.ID)).Select(value => ((byte)9, value.ID)));
        return [.. values.OrderBy(value => value.TypeId).ThenBy(value => value.MetaId)];
    }

    /// <summary>装备的本地化名称（任意可发放类型）；不存在时退回 MetaId。</summary>
    public static string GetLocalizedName(byte typeId, int metaId) => typeId switch
    {
        1 or 2 or 3 => Find(typeId, metaId)?.GetLocalizedName()
            ?? metaId.ToString(CultureInfo.InvariantCulture),
        5 => GameTableCatalog.Instance.GetDataById<RoleData>(metaId)?.GetLocalizedName()
            ?? metaId.ToString(CultureInfo.InvariantCulture),
        6 => GameTableCatalog.Instance.GetDataById<EmblemData>(metaId)?.GetLocalizedName()
            ?? metaId.ToString(CultureInfo.InvariantCulture),
        9 => GameTableCatalog.Instance.GetDataById<PetChipData>(metaId)?.GetLocalizedName()
            ?? metaId.ToString(CultureInfo.InvariantCulture),
        _ => metaId.ToString(CultureInfo.InvariantCulture),
    };
}

public sealed class WeaponDataV3 : EquipmentTableBase
{
    public const string TablePath = "Data/WeaponDataV3";

    public override byte TypeId { get; } = 1;

    public string BaseType { get; set; } = string.Empty;

    public string Anitype { get; set; } = string.Empty;

    public override void Verification()
    {
        base.Verification();

        // 哨兵行执行一次装备域级校验：养成核心表非空、存在升星材料与升格档、存在可发放装备。
        if (!ReferenceEquals(GameTableCatalog.Instance.GetAllData<WeaponDataV3>()[0], this))
        {
            return;
        }

        if (GameTableCatalog.Instance.GetAllData<PowerupExpTypesData>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<EquipEvolveData>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<PromoteData>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<SkillUpMaterialData>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<RoleDataV2>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<RoleLevelData>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<RoleSkillData>().Count == 0 ||
            GameTableCatalog.Instance.GetAllData<TextMapData>().Count == 0)
        {
            throw new InvalidDataException("装备养成或 RoleV2 资源表为空");
        }
        if (GameTableCatalog.Instance.GetAllData<StackMaterialData>().All(value => !value.IsStarUpMaterial))
        {
            throw new InvalidDataException("StackMaterialData 缺少 MaterialType 7 升星材料");
        }
        if (GameTableCatalog.Instance.GetAllData<PromoteData>().All(value => !value.IsUnlockTier))
        {
            throw new InvalidDataException("PromoteData 缺少带材料的升格突破档");
        }
        if (GetGrantableIds().Count == 0)
        {
            throw new InvalidDataException("当前资源没有可发放装备");
        }
    }
}

public sealed class CostumeDataV2 : EquipmentTableBase
{
    public const string TablePath = "Data/CostumeDataV2";

    public override byte TypeId { get; } = 2;

    public string BodyMod { get; set; } = string.Empty;
}

/// <summary>
/// 徽章装备（类型 3）。表名沿用客户端资源名 PassiveSkillDataV3，因为本作徽章的效果就是被动技能；
/// 它与 <see cref="EmblemData"/>（类型 6，学生证上的萌章）是两套互不相干的系统，不要混用。
/// </summary>
public sealed class PassiveSkillDataV3 : EquipmentTableBase
{
    public const string TablePath = "Data/PassiveSkillDataV3";

    public override byte TypeId { get; } = 3;
}

public sealed class SpecialAttributeDataV2 : TableBase
{
    public const string TablePath = "Data/SpecialAttributeDataV2";

    public int ID { get; set; }

    public override int GetId() => ID;

    public string Name { get; set; } = string.Empty;

    public string DisplayTitle { get; set; } = string.Empty;

    public string DisplayDescription { get; set; } = string.Empty;

    public int SlotCount { get; set; }

    public List<int> Slot1Equips { get; set; } = [];

    public int Slot1MaxLevel { get; set; }

    public List<int> Slot2Equips { get; set; } = [];

    public int Slot2MaxLevel { get; set; }

    public List<int> Slot3Equips { get; set; } = [];

    public int Slot3MaxLevel { get; set; }

    public List<int> Slot4Equips { get; set; } = [];

    public int Slot4MaxLevel { get; set; }

    public List<int> Slot5Equips { get; set; } = [];

    public int Slot5MaxLevel { get; set; }

    public string Feature { get; set; } = string.Empty;

    public int MultipleExistence { get; set; }

    public int GetMaxLevelForEquipment(int equipmentMetaId)
    {
        int maximum = 0;
        ApplySlot(Slot1Equips, Slot1MaxLevel);
        ApplySlot(Slot2Equips, Slot2MaxLevel);
        ApplySlot(Slot3Equips, Slot3MaxLevel);
        ApplySlot(Slot4Equips, Slot4MaxLevel);
        ApplySlot(Slot5Equips, Slot5MaxLevel);
        return maximum;

        void ApplySlot(IReadOnlyCollection<int> equipmentIds, int maxLevel)
        {
            // 0 表示该技能槽对所有装备开放。
            if (maxLevel > maximum && (equipmentIds.Contains(0) || equipmentIds.Contains(equipmentMetaId)))
            {
                maximum = maxLevel;
            }
        }
    }

    public IReadOnlyList<int> GetActiveSlotMaxLevels() =>
        new[] { Slot1MaxLevel, Slot2MaxLevel, Slot3MaxLevel, Slot4MaxLevel, Slot5MaxLevel }
            .Take(Math.Clamp(SlotCount, 0, 5))
            .ToArray();

    public override void Verification()
    {
        if (ID <= 0 || SlotCount is < 0 or > 5 || GetActiveSlotMaxLevels().Any(value => value < 0))
        {
            throw new InvalidDataException($"SpecialAttributeDataV2 {ID} 的活跃槽位配置非法");
        }
    }
}

/// <summary>
/// 普通堆叠材料。结算只读取库存上限和客户端掉落许可，不在协议层保存表对象。
/// </summary>
public sealed class StackMaterialData : TableBase
{
    public const string TablePath = "Data/StackMaterialData";
    /// <summary>MaterialType 7 = 装备升星材料。</summary>
    public const int StarUpMaterialType = 7;

    public string Name { get; set; } = string.Empty;

    public int ID { get; set; }

    public override int GetId() => ID;

    public int MaxStock { get; set; }

    public int Rarity { get; set; }

    public int MaterialType { get; set; }

    public int MaterialSubType { get; set; }

    public int DisplayTitle { get; set; }

    public int ExpProvideBase { get; set; }

    public int ExpProvideType { get; set; }

    public int PromoteExpProvide { get; set; }

    public bool AllowClientDrop { get; set; }

    /// <summary>是否为装备升星材料（MaterialType=7）。</summary>
    public bool IsStarUpMaterial => ID > 0 && MaterialType == StarUpMaterialType;
}

/// <summary>
/// 萌章（装备类型 6）：挂在学生证上的装饰收藏，不是背包里的徽章装备（那是 <see cref="PassiveSkillDataV3"/>）。
/// 只有 MetaId 5001--5999 是玩家可持有的萌章，1--18 是学生证空位与 VIP 位；萌章整体不占背包槽。
/// </summary>
public sealed class EmblemData : TableBase
{
    public const string TablePath = "Data/EmblemData";

    /// <summary>客户端文本键前缀；资源里存的是 TEXT27001 这种键，去掉前缀即 TextMap ID。</summary>
    private const string TextKeyPrefix = "TEXT";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int ItemType { get; set; }

    public string DisplayTitle { get; set; } = string.Empty;

    public int DisplayImage { get; set; }

    public int Rare { get; set; }

    public bool AllowClientDrop { get; set; }

    /// <summary>可持有的萌章要求 ItemType=6。</summary>
    public bool IsEquipment => ItemType == 6;

    /// <summary>本地化名称：DisplayTitle 是客户端文本键，按 TextMap 解析；解析不到时退回 MetaId。</summary>
    public string GetLocalizedName()
    {
        if (DisplayTitle.StartsWith(TextKeyPrefix, StringComparison.Ordinal) &&
            int.TryParse(DisplayTitle.AsSpan(TextKeyPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int textId) &&
            textId > 0 &&
            GameTableCatalog.Instance.GetDataById<TextMapData>(textId)?.CONTENT is { Length: > 0 } localized)
        {
            return localized;
        }

        return ID.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class PetChipData : TableBase
{
    public const string TablePath = "Data/PetChipData";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int Rarity { get; set; }

    public int PetID { get; set; }

    public int CombineNumber { get; set; }

    public int DisplayTitle { get; set; }

    public int DisplayDescription { get; set; }

    public int DisplayNumber { get; set; }

    public int DisplayImage { get; set; }

    public int DisplayDrop { get; set; }

    public int ResolveNumber { get; set; }

    /// <summary>本地化名称：TextMap 标题，缺省退回 MetaId。</summary>
    public string GetLocalizedName()
    {
        if (DisplayTitle > 0 && GameTableCatalog.Instance.GetDataById<TextMapData>(DisplayTitle)?.CONTENT is { } content)
        {
            return content;
        }
        return ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
