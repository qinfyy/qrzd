namespace Sv.Resources.Tables;

public sealed class DlcRoleData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcRoleData";
    public int Level { get; set; }
    public int Exp { get; set; }
    public float Hp { get; set; }
    public float Atk { get; set; }
    public float Def { get; set; }
    public override int GetId() => Level;

    public override void Verification()
    {
        if (Exp < 0)
        {
            throw new InvalidDataException($"逐火 DLC 角色等级 {Level} 经验为负");
        }

        // 哨兵行执行一次表级校验：等级曲线必须从 1 连续到最大等级。
        IReadOnlyList<DlcRoleData> levels = GameTableCatalog.Instance.GetAllData<DlcRoleData>();
        if (ReferenceEquals(levels[0], this) && !Enumerable.Range(1, levels.Max(value => value.Level)).All(level => levels.Any(value => value.Level == level)))
        {
            throw new InvalidDataException("逐火 DLC 角色等级曲线不连续");
        }
    }
}

public sealed class DlcRoleSettingData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcRoleSetting";
    public int RoleID { get; set; }
    public int SecondName { get; set; }
    public int FirstName { get; set; }
    public bool IsDefault { get; set; }
    public List<int> WeaponType { get; set; } = [];
    public List<int> InitEquip { get; set; } = [];
    public override int GetId() => RoleID;

    public override void Verification()
    {
        if (RoleID <= 0 || InitEquip.Where(value => value > 0).Any(value => GameTableCatalog.Instance.GetDataById<DlcEquipmentData>(value) is null))
        {
            throw new InvalidDataException($"逐火 DLC 角色 {RoleID} 初始装备引用非法");
        }

        // 哨兵行执行一次域级校验：逐火 DLC 的核心表非空且默认角色唯一。
        IReadOnlyList<DlcRoleSettingData> roles = GameTableCatalog.Instance.GetAllData<DlcRoleSettingData>();
        if (ReferenceEquals(roles[0], this))
        {
            if (roles.Count(value => value.IsDefault) != 1 ||
                GameTableCatalog.Instance.GetAllData<DlcRoleData>().Count == 0 ||
                GameTableCatalog.Instance.GetAllData<DlcEquipmentData>().Count == 0 ||
                GameTableCatalog.Instance.GetAllData<DlcAchieveData>().Count == 0 ||
                GameTableCatalog.Instance.GetAllData<DlcMissionData>().Count == 0 ||
                GameTableCatalog.Instance.GetAllData<DlcLevelMetaData>().Count == 0)
            {
                throw new InvalidDataException("逐火 DLC 核心资源为空或默认角色不唯一");
            }
        }
    }
}

public sealed class DlcEquipmentData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcEquipmentData";
    public int Id { get; set; }
    public int Type { get; set; }
    public int SubType { get; set; }
    public List<int> UnlockAbility { get; set; } = [];
    public int Image { get; set; }
    public int Name { get; set; }
    public int Description { get; set; }
    public float WeaponAtk { get; set; }
    public float AtkAdd { get; set; }
    public float HpAdd { get; set; }
    public float DefAdd { get; set; }
    public float CriticalAdd { get; set; }
    public float CritDmgAdd { get; set; }
    public int Ability1 { get; set; }
    public int Ability2 { get; set; }
    public int Ability3 { get; set; }
    public int Ability4 { get; set; }
    public int Ability5 { get; set; }
    public List<int> RuneID { get; set; } = [];
    public override int GetId() => Id;

    public IEnumerable<int> AbilityIds =>
        new[] { Ability1, Ability2, Ability3, Ability4, Ability5 }.Where(value => value > 0);

    public override void Verification()
    {
        if (Id <= 0 || Type is < 1 or > 4 || AbilityIds.Any(value => GameTableCatalog.Instance.GetDataById<DlcAllAbilityData>(value) is null))
        {
            throw new InvalidDataException($"逐火 DLC 装备 {Id} 配置非法");
        }
    }
}

public sealed class DlcAllAbilityData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcAllAbilityData";
    public int Id { get; set; }
    public int Type { get; set; }
    public int CalType { get; set; }
    public override int GetId() => Id;
}

public sealed class DlcTalentData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcTalentData";
    public int Id { get; set; }
    public List<int> Parent { get; set; } = [];
    public int Category { get; set; }
    public int CostSoul { get; set; }
    public bool IsKey { get; set; }
    public override int GetId() => Id;

    public override void Verification()
    {
        if (CostSoul < 0 || Parent.Where(value => value > 0).Any(value => GameTableCatalog.Instance.GetDataById<DlcTalentData>(value) is null))
        {
            throw new InvalidDataException($"逐火 DLC 天赋 {Id} 配置非法");
        }
    }
}

public sealed class DlcRuneData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcRuneData";
    public int Id { get; set; }
    public int GroupID { get; set; }
    public int Parent { get; set; }
    public int Category { get; set; }
    public int CostSoul { get; set; }
    public override int GetId() => Id;

    public override void Verification()
    {
        if (CostSoul < 0 || Parent > 0 && GameTableCatalog.Instance.GetDataById<DlcRuneData>(Parent) is null)
        {
            throw new InvalidDataException($"逐火 DLC 符文 {Id} 配置非法");
        }
    }
}

public sealed class DlcRuneLinkData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcRuneLinkData";
    public int GroupID { get; set; }
    public List<string> LinkType { get; set; } = [];
    public List<int> LevelRequire { get; set; } = [];
    public override int GetId() => GroupID;
}

public sealed class DlcAchieveData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcAchieveData";
    public int SubType { get; set; }
    public int AchieveID { get; set; }
    public int Order { get; set; }
    public int Tag { get; set; }
    public int Progress { get; set; }
    public int CalculateType { get; set; }
    public int Para1 { get; set; }
    public int Para2 { get; set; }
    public int Para3 { get; set; }
    public int Para4 { get; set; }
    public int Para5 { get; set; }
    public int HardCoin { get; set; }
    public int SoftCoin { get; set; }
    public int Soul { get; set; }
    public override int GetId() => AchieveID;
}

public sealed class DlcMissionData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcMissionData";
    public int MissionID { get; set; }
    public List<int> ParentID { get; set; } = [];
    public List<int> ParentStoryID { get; set; } = [];
    public List<int> ParentLevel { get; set; } = [];
    public int ShowType { get; set; }
    public int DependType { get; set; }
    public int LevelRequiredMin { get; set; }
    public int Chapter { get; set; }
    public int NameText { get; set; }
    public int DespText { get; set; }
    public int StoryIDStart { get; set; }
    public int StoryIDEnd { get; set; }
    public List<int> LevelBond { get; set; } = [];
    public List<int> LevelUnlock { get; set; } = [];
    public List<int> AchieveUnlock { get; set; } = [];
    public int SubType { get; set; }
    public int Progress { get; set; }
    public int Para1 { get; set; }
    public List<int> Para2 { get; set; } = [];
    public int Para3 { get; set; }
    public int Para4 { get; set; }
    public int Para5 { get; set; }
    public int Para6 { get; set; }
    public int RewardItemID1 { get; set; }
    public int RewardItemID2 { get; set; }
    public int RewardItemID3 { get; set; }
    public int RewardItemID4 { get; set; }
    public int RewardItemID5 { get; set; }
    public int NeedCheckPoint { get; set; }
    public override int GetId() => MissionID;

    public IEnumerable<int> RewardIds =>
        new[] { RewardItemID1, RewardItemID2, RewardItemID3, RewardItemID4, RewardItemID5 }
            .Where(value => value > 0);

    public override void Verification()
    {
        if (Progress <= 0 || SubType is < 3001 or > 3005 ||
            ParentID.Where(value => value > 0).Any(value => GameTableCatalog.Instance.GetDataById<DlcMissionData>(value) is null) ||
            LevelBond.Concat(LevelUnlock).Where(value => value > 0).Any(value =>
                GameTableCatalog.Instance.GetDataById<DlcLevelMetaData>(value) is null))
        {
            throw new InvalidDataException($"逐火 DLC 任务 {MissionID} 配置非法");
        }

        foreach (int rewardId in RewardIds)
        {
            RewardItemData reward = GameTableCatalog.Instance.GetDataById<RewardItemData>(rewardId)
                ?? throw new InvalidDataException($"逐火 DLC 任务 {MissionID} 奖励 {rewardId} 不存在");
            bool supported = reward.AwardType == 4 ||
                             reward.AwardType == 46 ||
                             reward.AwardType == 47 && GameTableCatalog.Instance.GetDataById<DlcEquipmentData>(reward.EquipID) is not null ||
                             reward.AwardType == 30 && reward.EquipType == 250;
            if (!supported || reward.Num < 0)
                throw new InvalidDataException($"逐火 DLC 任务 {MissionID} 奖励 {rewardId} 组合未支持");
        }
    }
}

public sealed class DlcLevelMetaData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DlcLevelMeta";
    public int ID { get; set; }
    public int Type { get; set; }
    public int LevelTypeForServer { get; set; }
    public bool InitUnlock { get; set; }
    public List<int> ParentId { get; set; } = [];
    public int District { get; set; }
    public int Order { get; set; }
    public int Title { get; set; }
    public int Description { get; set; }
    public int RecommendedLevel { get; set; }
    public int MinPassTime { get; set; }
    public int MaxExp { get; set; }
    public int MaxSoftCoin { get; set; }
    public int MaxPerkPoint { get; set; }
    public bool RepeatDisabled { get; set; }
    public override int GetId() => ID;

    public override void Verification()
    {
        if (ID <= 0 || Type is not (1 or 4) || MinPassTime < 0 || MaxExp < 0 || MaxSoftCoin < 0 || MaxPerkPoint < 0)
        {
            throw new InvalidDataException($"逐火 DLC 关卡 {ID} 存在非法字段");
        }
    }
}
