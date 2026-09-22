namespace Sv.Resources.Tables;

/// <summary>
/// 常规助战配置。客户端用它生成剧情关卡中的“崩坏娘”类人机助战。
/// </summary>
public sealed class RegularAssistEquipData : TableBase
{
    public const string TablePath = "Data/RegularAssistEquipe";

    public int KeyID { get; set; }

    public int GradeID { get; set; }

    public int Name { get; set; }

    public int Emblem { get; set; }

    public int Icon { get; set; }

    public int DefaultEquipID { get; set; }

    public override int GetId() => DefaultEquipID;

    public List<int> LevelID { get; set; } = [];

    public override void Verification()
    {
        DefaultEquipData equip = GameTableCatalog.Instance.GetDataById<DefaultEquipData>(DefaultEquipID)
            ?? throw new InvalidDataException(
                $"RegularAssistEquipe {KeyID} 引用了不存在的默认套装 {DefaultEquipID}");
        if (GameTableCatalog.Instance.GetDataById<RoleData>(equip.Role) is null ||
            GameTableCatalog.Instance.GetDataById<CostumeDataV2>(equip.Costume) is null ||
            equip.Weapon.Any(value => value != 0 && GameTableCatalog.Instance.GetDataById<WeaponDataV3>(value) is null) ||
            equip.Passive.Any(value => value != 0 && GameTableCatalog.Instance.GetDataById<PassiveSkillDataV3>(value) is null) ||
            LevelID.Any(value => GameTableCatalog.Instance.GetDataById<LevelMetaV2>(value) is null))
        {
            throw new InvalidDataException(
                $"RegularAssistEquipe {KeyID} 的默认套装 {DefaultEquipID} 存在无效装备引用");
        }

        int[][] equipmentSkills =
        [
            [.. EquipmentTableBase.GetSkillIds(2, equip.Costume)],
            .. equip.Weapon
                .Where(value => value != 0)
                .Select(value => EquipmentTableBase.GetSkillIds(1, value).ToArray()),
            .. equip.Passive
                .Where(value => value != 0)
                .Select(value => EquipmentTableBase.GetSkillIds(3, value).ToArray()),
            .. equip.RoleSkill.Where(value => value != 0).Select(value => new[] { value }),
        ];
        if (equipmentSkills.SelectMany(value => value).Any(value => GameTableCatalog.Instance.GetDataById<SpecialAttributeDataV2>(value) is null))
        {
            throw new InvalidDataException(
                $"RegularAssistEquipe {KeyID} 的默认套装 {DefaultEquipID} 存在无效技能引用");
        }

        if (LevelID.Count == 0)
        {
            throw new InvalidDataException($"RegularAssistEquipe {KeyID} 没有适用关卡");
        }
    }
}

/// <summary>
/// 助战默认套装。字段名保持策划表命名，列表顺序由客户端表定义。
/// </summary>
public sealed class DefaultEquipData : TableBase
{
    public const string TablePath = "Data/DefaultEquipData";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int Role { get; set; }

    public int SkinID { get; set; }

    public int Costume { get; set; }

    public List<int> Weapon { get; set; } = [];

    public List<int> Passive { get; set; } = [];

    public int EquipmentLevel { get; set; }

    public int RoleLevel { get; set; }

    public List<int> RoleSkill { get; set; } = [];
}
