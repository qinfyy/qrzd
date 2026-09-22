using System.Globalization;
using CsvHelper.Configuration.Attributes;
using Sv.Utility;

namespace Sv.Resources.Tables;

public sealed class PowerupExpTypesData : TableBase
{
    public const string TablePath = "Data/PowerupExpTypes";

    [Name("LEVEL", "Level", "LV", "Lv", "level", "lv")]
    public int Level { get; set; }

    public override int GetId() => Level;

    [Name("EXP1", "Exp1(100w)")]
    public int EXP1 { get; set; }

    [Name("EXP2", "Exp2(150w)")]
    public int EXP2 { get; set; }

    [Name("EXP3", "Exp3(200w)")]
    public int EXP3 { get; set; }

    [Name("EXP4", "Exp4(250w)")]
    public int EXP4 { get; set; }

    [Name("EXP5", "Exp5(300w)")]
    public int EXP5 { get; set; }

    [Name("EXP6", "Exp6(400w)")]
    public int EXP6 { get; set; }

    [Name("EXP7", "Exp7(500w)")]
    public int EXP7 { get; set; }

    public int GetExpByType(int expType) => expType switch
    {
        1 => EXP1,
        2 => EXP2,
        3 => EXP3,
        4 => EXP4,
        5 => EXP5,
        6 => EXP6,
        7 => EXP7,
        _ => throw new ArgumentOutOfRangeException(nameof(expType), expType, "装备经验类型只支持 1 到 7"),
    };

    public override void Verification()
    {
        if (Enumerable.Range(1, 7).Any(expType => GetExpByType(expType) < 0))
        {
            throw new InvalidDataException($"PowerupExpTypes 等级 {Level} 存在负经验");
        }
    }
}

public sealed class EquipEvolveData : TableToolsTableBase
{
    public const string TablePath = "Data/EvolveData";

    private const string StartTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public int ID { get; set; }

    public int Input { get; set; }

    public int Output { get; set; }

    public int moneyCost { get; set; }

    public int NumMaterials { get; set; }

    public List<int> Material1 { get; set; } = [];

    public int Material1Num { get; set; }

    public List<int> Material2 { get; set; } = [];

    public int Material2Num { get; set; }

    public List<int> Material3 { get; set; } = [];

    public int Material3Num { get; set; }

    public List<int> Material4 { get; set; } = [];

    public int Material4Num { get; set; }

    public List<int> Material5 { get; set; } = [];

    public int Material5Num { get; set; }

    public int evolvetype { get; set; }

    public int SkillRequire { get; set; }

    public int isOpen { get; set; }

    public string StartTime { get; set; } = string.Empty;

    public sealed record EquipEvolveMaterialGroup(IReadOnlyList<int> MaterialIds, int Amount);

    public override int GetId() => ID;

    public bool IsSupportedEquipmentEvolve => evolvetype is 1 or 2 or 3;
    

    /// <summary>配方是否已开放：表内 StartTime 是不带时区的时间文本，按配置时区解释，解析失败视为已开放。</summary>
    public bool IsOpenAt(DateTimeOffset now)
    {
        if (isOpen == 0)
        {
            return false;
        }

        if (!DateTime.TryParseExact(StartTime, StartTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startTime))
        {
            return true;
        }

        return now >= Time.FromLocal(startTime);
    }

    private static void AddGroup(ICollection<EquipEvolveMaterialGroup> groups, IReadOnlyList<int> materialIds, int amount)
    {
        int[] ids = materialIds.Where(value => value > 0).Distinct().ToArray();
        // ID 或数量任一为零都是占位；只有正 ID 与正数量同时存在才构成材料组。
        if (ids.Length > 0 && amount > 0)
        {
            groups.Add(new EquipEvolveMaterialGroup(Array.AsReadOnly(ids), amount));
        }
    }

    public IReadOnlyList<EquipEvolveMaterialGroup> GetMaterialGroups()
    {
        List<EquipEvolveMaterialGroup> groups = [];
        AddGroup(groups, Material1, Material1Num);
        AddGroup(groups, Material2, Material2Num);
        AddGroup(groups, Material3, Material3Num);
        AddGroup(groups, Material4, Material4Num);
        AddGroup(groups, Material5, Material5Num);
        return groups;
    }

    public bool MatchesMaterialCounts(IReadOnlyDictionary<int, int> materialCounts)
    {
        Dictionary<int, int> remaining = materialCounts
            .Where(value => value.Key > 0 && value.Value > 0)
            .ToDictionary(value => value.Key, value => value.Value);
        IReadOnlyList<EquipEvolveMaterialGroup> groups = GetMaterialGroups();
        return MatchGroup(0);

        bool MatchGroup(int groupIndex)
        {
            if (groupIndex == groups.Count)
            {
                return remaining.Count == 0;
            }

            EquipEvolveMaterialGroup group = groups[groupIndex];
            foreach (int materialId in group.MaterialIds.Order())
            {
                int current = remaining.GetValueOrDefault(materialId);
                if (current < group.Amount)
                {
                    continue;
                }

                int left = current - group.Amount;
                if (left == 0)
                {
                    remaining.Remove(materialId);
                }
                else
                {
                    remaining[materialId] = left;
                }

                if (MatchGroup(groupIndex + 1))
                {
                    return true;
                }

                remaining[materialId] = current;
            }

            return false;
        }
    }

    public string GetCanonicalSignature()
    {
        string groups = string.Join('|', GetMaterialGroups()
            .Select(value => $"{string.Join(',', value.MaterialIds.Order())}:{value.Amount}")
            .Order(StringComparer.Ordinal));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Input}>{Output};{moneyCost};{evolvetype};{SkillRequire};{groups}");
    }

    public IReadOnlySet<string> GetConcreteMaterialSignatures()
    {
        IReadOnlyList<EquipEvolveMaterialGroup> groups = GetMaterialGroups();
        HashSet<string> signatures = new(StringComparer.Ordinal);
        Dictionary<int, int> counts = [];
        Expand(0);
        return signatures;

        void Expand(int groupIndex)
        {
            if (groupIndex == groups.Count)
            {
                signatures.Add(string.Join(',', counts
                    .OrderBy(value => value.Key)
                    .Select(value => $"{value.Key}:{value.Value}")));
                return;
            }

            EquipEvolveMaterialGroup group = groups[groupIndex];
            foreach (int materialId in group.MaterialIds)
            {
                int before = counts.GetValueOrDefault(materialId);
                counts[materialId] = checked(before + group.Amount);
                Expand(groupIndex + 1);
                if (before == 0)
                {
                    counts.Remove(materialId);
                }
                else
                {
                    counts[materialId] = before;
                }
            }
        }
    }

    public override void Verification()
    {
        if (!IsSupportedEquipmentEvolve)
        {
            return;
        }

        if (!RoleDataV2.HasKnownEquipmentMetaId(Input) || !RoleDataV2.HasKnownEquipmentMetaId(Output))
        {
            throw new InvalidDataException(
                $"EvolveData {ID} 引用了不存在的输入/输出装备 {Input}->{Output}");
        }

        if (moneyCost < 0 || NumMaterials is < 0 or > 5 || SkillRequire < 0)
        {
            throw new InvalidDataException($"EvolveData {ID} 的消耗配置非法");
        }

        foreach (EquipEvolveMaterialGroup group in GetMaterialGroups())
        {
            if (group.Amount <= 0 || group.MaterialIds.Count == 0 || group.MaterialIds.Any(value => !HasKnownMaterialMetaId(value)))
            {
                throw new InvalidDataException($"EvolveData {ID} 存在非法材料配置");
            }
        }

        // 哨兵行执行一次表级校验：同一 (Input,Output) 的开放配方不允许材料多重集歧义，
        // 否则按请求材料匹配配方（EquipmentDevelopmentLogic）无法确定唯一配方。
        IReadOnlyList<EquipEvolveData> rows = GameTableCatalog.Instance.GetAllData<EquipEvolveData>();
        if (!ReferenceEquals(rows[0], this))
        {
            return;
        }

        foreach (IGrouping<(int Input, int Output), EquipEvolveData> candidates in rows
                     .Where(value => value.IsSupportedEquipmentEvolve && value.isOpen != 0)
                     .GroupBy(value => (value.Input, value.Output)))
        {
            Dictionary<string, string> recipeByMaterials = new(StringComparer.Ordinal);
            foreach (EquipEvolveData recipe in candidates)
            {
                string canonicalRecipe = recipe.GetCanonicalSignature();
                foreach (string materials in recipe.GetConcreteMaterialSignatures())
                {
                    if (recipeByMaterials.TryGetValue(materials, out string? existing) && !string.Equals(existing, canonicalRecipe, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"装备进化 {candidates.Key.Input}->{candidates.Key.Output} 存在材料多重集歧义");
                    }
                    recipeByMaterials[materials] = canonicalRecipe;
                }
            }
        }
    }

    /// <summary>材料 metaId 在装备表与堆叠材料表中的联合存在性判定。</summary>
    internal static bool HasKnownMaterialMetaId(int metaId) =>
        RoleDataV2.HasKnownEquipmentMetaId(metaId) ||
        GameTableCatalog.Instance.GetDataById<StackMaterialData>(metaId) is not null;
}

public sealed class PromoteData : TableToolsTableBase
{
    public const string TablePath = "Data/PromoteData";

    public int promoteLv { get; set; }

    public int promoteExp { get; set; }

    public int promoteGold { get; set; }

    public List<int> promoteMaterial { get; set; } = [];

    public float weaponAddAtk { get; set; }

    public float weaponAddAmmo { get; set; }

    public float costumeAdd { get; set; }

    public float passiveSkillAdd { get; set; }

    public float weaponExtraAtk { get; set; }

    public float weaponExtraAmmo { get; set; }

    public float weaponExtraAtkSpd { get; set; }

    public float weaponExtraCrit { get; set; }

    public float weaponExtraPlaceFireRate { get; set; }

    public List<int> equipPromoteList { get; set; } = [];

    public override int GetId() => promoteLv;

    /// <summary>带材料的升格突破档；这些档位构成装备等级上限的解锁序列。</summary>
    public bool IsUnlockTier => promoteLv > 0 && promoteMaterial.Any(id => id > 0);

    public override void Verification()
    {
        if (promoteLv < 0 || promoteExp < 0 || promoteGold < 0)
        {
            throw new InvalidDataException($"PromoteData 等级 {promoteLv} 存在负数配置");
        }
    }
}

public sealed class SkillUpMaterialData : TableToolsTableBase
{
    public const string TablePath = "Data/SkillUpMaterial";
    public const int EquipmentMaterialId = 1;

    public int ID { get; set; }

    public int Type { get; set; }

    public string Params { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public SkillUpTargetType TargetType => Type switch
    {
        >= 0 and <= 9 => (SkillUpTargetType)Type,
        _ => SkillUpTargetType.Invalid,
    };

    public bool TryGetIntegerParams(out int[] values)
    {
        string[] parts = Params.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        values = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], CultureInfo.InvariantCulture, out values[index]))
            {
                values = [];
                return false;
            }
        }
        return true;
    }

    public override int GetId() => ID;

    public override void Verification()
    {
        bool isEquipmentMaterial = ID == EquipmentMaterialId && TargetType == SkillUpTargetType.Equipment;
        if (ID <= 0 || TargetType == SkillUpTargetType.Invalid || DisplayOrder < 0 || (TargetType == SkillUpTargetType.Equipment && !isEquipmentMaterial) || (!isEquipmentMaterial && GameTableCatalog.Instance.GetDataById<StackMaterialData>(ID) is null))
        {
            throw new InvalidDataException($"SkillUpMaterial {ID} 配置非法");
        }

        if (Type == 2)
        {
            if (!TryGetIntegerParams(out int[] parameters) || parameters.Length == 0 || parameters.Any(value => value <= 0 || !RoleDataV2.HasKnownEquipmentMetaId(value)))
            {
                throw new InvalidDataException($"SkillUpMaterial {ID} 的参数 {Params} 配置非法");
            }
        }
        else if (Type == 3 && (!TryGetIntegerParams(out int[] parameters) || parameters.Length != 1 || parameters[0] <= 0))
        {
            throw new InvalidDataException($"SkillUpMaterial {ID} 的参数 {Params} 配置非法");
        }
    }

    public enum SkillUpTargetType
    {
        Invalid = -1,
        Equipment = 0,
        AllEquipEnsure = 1,
        EquipByIdEnsure = 2,
        AllEquipEnsureToLevel = 3,
        AllEquip = 4,
        EquipByMainType = 5,
        WeaponByBaseType = 6,
        EquipBySeries = 7,
        EquipById = 8,
        SkillForEx = 9,
    }
}

public sealed class RoleDataV2 : TableToolsTableBase
{
    public const string TablePath = "Data/RoleDataV2";

    public int roleID { get; set; }

    public int roleName { get; set; }

    public int description { get; set; }

    public int boundWeapon { get; set; }

    public List<string> limitWeapon { get; set; } = [];

    public int partnerOffsetID { get; set; }

    public int partnerPosterID { get; set; }

    public override int GetId() => roleID;

    /// <summary>该角色的等级曲线（按 level 升序、从 1 连续），来自 RoleLevelData。</summary>
    [Ignore]
    public IReadOnlyList<RoleLevelData> Levels { get; private set; } = [];

    /// <summary>该角色的技能（按 skillType、skillID 升序），来自 RoleSkillData。</summary>
    [Ignore]
    public IReadOnlyList<RoleSkillData> Skills { get; private set; } = [];

    /// <summary>该角色的限解（特训轨迹）档位（按 DuplicateNum 升序），来自 RoleDuplicateData。</summary>
    [Ignore]
    public IReadOnlyList<RoleDuplicateData> Duplicates { get; private set; } = [];

    /// <summary>该角色的任务点奖励档（按 Progress 升序），来自 RoleMissionRewardData。</summary>
    [Ignore]
    public IReadOnlyList<RoleMissionRewardData> MissionRewards { get; private set; } = [];

    public override void OnFinalize()
    {
        Levels = GameTableCatalog.Instance.GetAllData<RoleLevelData>()
            .Where(value => value.roleID == roleID)
            .OrderBy(value => value.level)
            .ToArray();
        Skills = GameTableCatalog.Instance.GetAllData<RoleSkillData>()
            .Where(value => value.roleID == roleID)
            .OrderBy(value => value.skillType)
            .ThenBy(value => value.skillID)
            .ToArray();
        Duplicates = GameTableCatalog.Instance.GetAllData<RoleDuplicateData>()
            .Where(value => value.RoleID == roleID)
            .OrderBy(value => value.DuplicateNum)
            .ToArray();
        MissionRewards = GameTableCatalog.Instance.GetAllData<RoleMissionRewardData>()
            .Where(value => value.RoleID == roleID)
            .OrderBy(value => value.Progress)
            .ToArray();
    }

    public override void Verification()
    {
        if (!HasKnownEquipmentMetaId(roleID) || !TextMapData.Exists(roleName) || !TextMapData.Exists(description) || (boundWeapon != 0 && GameTableCatalog.Instance.GetDataById<WeaponDataV3>(boundWeapon) is null))
        {
            throw new InvalidDataException($"RoleDataV2 {roleID} 存在无效角色、文本或绑定武器引用");
        }

        if (Levels.Count == 0 || Levels[0].level != 1 || !Levels.Select(value => value.level).SequenceEqual(Enumerable.Range(1, Levels.Count)))
        {
            throw new InvalidDataException($"RoleLevelData {roleID} 等级序列不连续");
        }

        ILookup<int, RoleSkillData> skillGroups = Skills.ToLookup(value => value.skillType);
        if (!skillGroups.Select(value => value.Key).Order().SequenceEqual(Enumerable.Range(0, 5)) || skillGroups[0].Count() != 1 || Enumerable.Range(1, 4).Any(skillType => skillGroups[skillType].Count() != 3))
        {
            throw new InvalidDataException($"RoleSkillData {roleID} 必须包含 type 0--4，且数量为 1/3/3/3/3");
        }
    }

    /// <summary>装备 metaId 跨四张装备表的联合存在性判定。</summary>
    public static bool HasKnownEquipmentMetaId(int metaId) =>
        metaId > 0 && (GameTableCatalog.Instance.GetDataById<WeaponDataV3>(metaId) is not null || GameTableCatalog.Instance.GetDataById<CostumeDataV2>(metaId) is not null || GameTableCatalog.Instance.GetDataById<PassiveSkillDataV3>(metaId) is not null || GameTableCatalog.Instance.GetDataById<RoleData>(metaId) is not null);
}

public sealed class RoleLevelData : TableToolsTableBase
{
    public const string TablePath = "Data/RoleLevelData";

    public int roleID { get; set; }

    public int level { get; set; }

    public int experience { get; set; }

    public string upGradeReward { get; set; } = string.Empty;

    public string weaponType { get; set; } = string.Empty;

    public float rewardPara { get; set; }

    public int CompositeId => checked((roleID * 10000) + level);

    public override int GetId() => CompositeId;

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<RoleDataV2>(roleID) is null || level <= 0 || experience < 0)
        {
            throw new InvalidDataException($"RoleLevelData {roleID}:{level} 配置非法");
        }
    }
}

public sealed class RoleSkillData : TableToolsTableBase
{
    public const string TablePath = "Data/RoleSkillData";

    public int skillID { get; set; }

    public int roleID { get; set; }

    public int skillType { get; set; }

    public int specialAttributeID { get; set; }

    public int name { get; set; }

    public int description { get; set; }

    public string icon { get; set; } = string.Empty;

    public int ultimateCD { get; set; }

    public int ultimateCharge { get; set; }

    public int ultimateLimit { get; set; }

    public float para1 { get; set; }

    public float para2 { get; set; }

    public float para3 { get; set; }

    public float para4 { get; set; }

    public float para5 { get; set; }

    public override int GetId() => skillID;

    /// <summary>该技能的解锁等级，来自 RoleLevelData 中 upGradeReward="Skill"、rewardPara=skillID 的行。</summary>
    [Ignore]
    public int UnlockLevel { get; private set; }

    public override void OnFinalize()
    {
        UnlockLevel = GameTableCatalog.Instance.GetAllData<RoleLevelData>()
            .Where(value => value.roleID == roleID && string.Equals(value.upGradeReward, "Skill", StringComparison.OrdinalIgnoreCase) && checked((int)value.rewardPara) == skillID)
            .Select(value => value.level)
            .DefaultIfEmpty(0)
            .Min();
    }

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<RoleDataV2>(roleID) is null || skillID <= 0 || skillType < 0 || (specialAttributeID != 0 && GameTableCatalog.Instance.GetDataById<SpecialAttributeDataV2>(specialAttributeID) is null) || !TextMapData.Exists(name) || !TextMapData.Exists(description))
        {
            throw new InvalidDataException($"RoleSkillData {skillID} 配置非法");
        }

        if (UnlockLevel <= 0)
        {
            throw new InvalidDataException($"RoleSkillData {roleID}:{skillID} 缺少同角色等级解锁配置");
        }
    }
}