using CsvHelper.Configuration.Attributes;
using Sv.Gateway.Packets;

namespace Sv.Resources.Tables;

/// <summary>关卡计划稀有掉落（RareDrop1-5 槽的结构化投影）。</summary>
public sealed record StoryRareDropDefinition(int Type, int Id, int Level, int Chance);

/// <summary>关卡或任务的首通奖励（RewardItemData 展开 AwardType=65 奖励组后的单行）。</summary>
public sealed record StoryRewardDefinition(int RewardId, int AwardType, int Number, int EquipType, int EquipId, int EquipLevel, int EquipStar, int EquipSkill);

public sealed class LevelMetaV2 : TableBase
{
    public const string TablePath = "Data/LevelMetaV2";

    /// <summary>普通剧情图根关卡（故事进度从这里开始向后展开）。</summary>
    public const int StoryRootLevelId = 4404;
    /// <summary>普通剧情图主类型（LevelTypeForServer）。</summary>
    public const int OrdinaryPrimaryServerType = 1;
    /// <summary>普通剧情图支线类型（LevelTypeForServer）。</summary>
    public const int OrdinarySecondaryServerType = 248;
    /// <summary>RP 玩法图类型（LevelTypeForServer）。</summary>
    public const int RolePlayServerType = 143;
    /// <summary>客户端掉落列表 140 字段的数量上限。</summary>
    public const int MaximumClientDropCount = 50;
    /// <summary>计划掉落概率的分母（万分比）。</summary>
    public const int PlannedDropChanceScale = 10000;
    /// <summary>普通剧情图关卡数量上限（140 同步包）。</summary>
    public const int MaximumStoryLevelCount = 2500;
    /// <summary>新玩家教学关卡段（122 入口）的首关 ID。</summary>
    public const int FirstNewPlayerTeachingLevelId = 4400;
    /// <summary>新玩家教学关卡段（122 入口）的末关 ID。</summary>
    public const int LastNewPlayerTeachingLevelId = 4404;
    /// <summary>新玩家教学关卡的 LevelTypeForServer。</summary>
    public const byte NewPlayerTeachingServerType = 147;

    public int Group { get; set; }

    public int ID { get; set; }

    public override int GetId() => ID;

    public int Type { get; set; }

    [Name("hardlevel")]
    public int HardLevel { get; set; }

    [Name("progressDepth")]
    public int ProgressDepth { get; set; }

    [Name("parentID")]
    public List<int> ParentId { get; set; } = [];

    [Name("isInitial")]
    public int IsInitial { get; set; }

    [Name("initUnlock")]
    public int InitUnlock { get; set; }

    [Name("stage")]
    public int Stage { get; set; }

    [Name("title")]
    public string Title { get; set; } = string.Empty;

    [Name("description")]
    public string Description { get; set; } = string.Empty;

    [Name("buttonTitle")]
    public string ButtonTitle { get; set; } = string.Empty;

    [Name("staminaCost")]
    public int StaminaCost { get; set; }

    [Name("maxExp")]
    public int MaxExp { get; set; }

    [Name("maxSoftCoin")]
    public int MaxSoftCoin { get; set; }

    [Name("maxHardCoin")]
    public int MaxHardCoin { get; set; }

    [Name("maxDropCount")]
    public int MaxDropCount { get; set; }

    public int NumRareDrops { get; set; }

    public int RareDrop1Type { get; set; }

    public int RareDrop1ID { get; set; }

    public int RareDrop1Level { get; set; }

    public int RareDrop1Chance { get; set; }

    public int RareDrop2Type { get; set; }

    public int RareDrop2ID { get; set; }

    public int RareDrop2Level { get; set; }

    public int RareDrop2Chance { get; set; }

    public int RareDrop3Type { get; set; }

    public int RareDrop3ID { get; set; }

    public int RareDrop3Level { get; set; }

    public int RareDrop3Chance { get; set; }

    public int RareDrop4Type { get; set; }

    public int RareDrop4ID { get; set; }

    public int RareDrop4Level { get; set; }

    public int RareDrop4Chance { get; set; }

    public int RareDrop5Type { get; set; }

    public int RareDrop5ID { get; set; }

    public int RareDrop5Level { get; set; }

    public int RareDrop5Chance { get; set; }

    public int LevelTypeForServer { get; set; }

    // 关卡挑战触发器 ID 列表（TSV 列 Triggers，0; 表示无）。客户端 66 上报的
    // _successTriggerList 即本场成功触发的该列表子集（SetupLevelFinishPacket
    // 0x818C85D 遍历 BaseTrigger 收集成功标记且 ID>0 的项）；52 个 601xxx 关卡
    // 各挂 1 个（主要是 RX3 的 601201-601240），其余主线关与全部 402xxx 为空。
    [Name("Triggers")]
    public List<int> Triggers { get; set; } = [];

    public int RecommendedLevel { get; set; }

    public int ClearReward1 { get; set; }

    public int ClearReward2 { get; set; }

    /// <summary>属于普通剧情图（StoryRootLevelId 后代且 LevelTypeForServer=1/248），即 storycompleted 可完成的关卡。</summary>
    [Ignore]
    public bool IsOrdinaryStory { get; internal set; }

    /// <summary>属于 RP 玩法图（LevelTypeForServer=143）。</summary>
    [Ignore]
    public bool IsRolePlay => LevelTypeForServer == RolePlayServerType;

    /// <summary>普通剧情图内的父关卡 ID（非零去重，引用闭合由 Verification 保证）。</summary>
    [Ignore]
    public IReadOnlyList<int> StoryParentIds { get; internal set; } = [];

    /// <summary>RP 玩法图的父关卡 ID（非零去重，仅要求存在于全表）。</summary>
    [Ignore]
    public IReadOnlyList<int> RolePlayParentIds { get; internal set; } = [];

    /// <summary>
    /// 所属玩法图的父关卡 ID：普通剧情与 RP 用各自图内集合，崩坏学园篇排除自身
    /// （parentID=self 是分链根标记）；不属于任何玩法图时为空。
    /// </summary>
    [Ignore]
    public IReadOnlyList<int> GraphParentIds
    {
        get
        {
            if (IsOrdinaryStory) return StoryParentIds;
            if (IsRolePlay) return RolePlayParentIds;
            if (IsNewMainStory) return [.. ParentId.Where(value => value != 0 && value != ID).Distinct().Order()];
            return [];
        }
    }

    /// <summary>计划稀有掉落（RareDrop1-5 槽结构化，空槽已剔除）。</summary>
    [Ignore]
    public IReadOnlyList<StoryRareDropDefinition> RareDrops { get; private set; } = [];

    /// <summary>
    /// 首通奖励展开（ClearReward1/2 经 RewardItemData 递归展开并校验类型组合）。
    /// 仅玩法图关卡（普通剧情/RP 在根行哨兵的 OnFinalize、崩坏学园篇在
    /// NewMainStoryMenuData/NewMainStoryChapterData 的 OnFinalize）装配期填充；
    /// 其余关卡不走预展开，运行时按需用 LevelManager 的无校验展开。
    /// </summary>
    [Ignore]
    public IReadOnlyList<StoryRewardDefinition> FirstClearRewards { get; internal set; } = [];

    /// <summary>所属崩坏学园篇章节 stage（NewMainStoryMenuData.StageName 的派生集合，9228 levels 语义）。</summary>
    [Ignore]
    public string? NewMainStoryStageName { get; internal set; }

    /// <summary>
    /// 崩坏学园篇主线章节 stage 集合中不在 NewMainStoryChapterData 布局表的关卡。
    /// 客户端章节视图 MonoUINewMainStoryChapterView.RefreshLevel（13.2.8_341 x86
    /// 0x9B870DA）对列表内每个关卡查 GetNewMainStoryChapterDataByLevelID，查不到直接抛
    /// NullReferenceException；因此这批关卡的 140 状态必须永远保持缺省（客户端未同步
    /// → state 5 → 被视图过滤）：服务端不为其播种/推进 LevelBin。
    /// </summary>
    [Ignore]
    public bool IsHiddenNewMainStoryLevel { get; internal set; }

    /// <summary>属于崩坏学园篇章节（stage 派生集合 ∪ NewMainStoryChapterData 布局表）。</summary>
    [Ignore]
    public bool IsNewMainStory { get; internal set; }

    public override void OnFinalize()
    {
        // RareDrop1-5 槽结构化投影，空槽（全零）剔除。
        StoryRareDropDefinition[] rareDrops =
        [
            new StoryRareDropDefinition(RareDrop1Type, RareDrop1ID, RareDrop1Level, RareDrop1Chance),
            new StoryRareDropDefinition(RareDrop2Type, RareDrop2ID, RareDrop2Level, RareDrop2Chance),
            new StoryRareDropDefinition(RareDrop3Type, RareDrop3ID, RareDrop3Level, RareDrop3Chance),
            new StoryRareDropDefinition(RareDrop4Type, RareDrop4ID, RareDrop4Level, RareDrop4Chance),
            new StoryRareDropDefinition(RareDrop5Type, RareDrop5ID, RareDrop5Level, RareDrop5Chance),
        ];
        RareDrops = Array.AsReadOnly(rareDrops.Where(HasDropData).ToArray());

        if (ID != StoryRootLevelId)
        {
            return;
        }

        // ---- 根行哨兵：构建普通剧情图与 RP 图的行级索引，并展开图内关卡的首通奖励 ----
        IReadOnlyList<LevelMetaV2> allLevels = GameTableCatalog.Instance.GetAllData<LevelMetaV2>();

        // 普通剧情图：从根做正向可达闭包，再按类型（1/248）过滤；
        // RP 图按类型 143 过滤即可，父集合仅要求存在于全表（引用闭合在 Verification 校验）。
        HashSet<int> reachable = [StoryRootLevelId];
        bool changed;
        do
        {
            changed = false;
            foreach (LevelMetaV2 candidate in allLevels)
            {
                if (!reachable.Contains(candidate.ID) && candidate.ParentId.Where(value => value != 0).Any(reachable.Contains))
                {
                    reachable.Add(candidate.ID);
                    changed = true;
                }
            }
        }
        while (changed);

        foreach (LevelMetaV2 meta in allLevels)
        {
            if (reachable.Contains(meta.ID) && meta.LevelTypeForServer is OrdinaryPrimaryServerType or OrdinarySecondaryServerType)
            {
                meta.IsOrdinaryStory = true;
                meta.StoryParentIds = Array.AsReadOnly([.. meta.ParentId.Where(value => value != 0).Distinct()]);
            }
            else if (meta.LevelTypeForServer == RolePlayServerType)
            {
                meta.RolePlayParentIds = Array.AsReadOnly([.. meta.ParentId.Where(value => value != 0).Distinct()]);
            }
            else
            {
                continue;
            }

            // 图内关卡展开首通奖励（ClearReward1/2 经 RewardItemData 递归展开并校验类型组合）。
            List<StoryRewardDefinition> rewards = [];
            RewardItemData.ExpandReward(meta.ClearReward1, 1, [], rewards);
            RewardItemData.ExpandReward(meta.ClearReward2, 1, [], rewards);
            meta.FirstClearRewards = Array.AsReadOnly(rewards.ToArray());
        }
    }

    public override void Verification()
    {
        if (IsOrdinaryStory)
        {
            if (StoryParentIds.Count == 0 || StoryParentIds.Any(value => GameTableCatalog.Instance.GetDataById<LevelMetaV2>(value) is null))
            {
                throw new InvalidDataException($"普通剧情关卡 {ID} 的前置关卡不存在或为空");
            }
            if (StoryParentIds.Any(value => value != StoryRootLevelId && GameTableCatalog.Instance.GetDataById<LevelMetaV2>(value)?.IsOrdinaryStory != true))
            {
                throw new InvalidDataException($"普通剧情关卡 {ID} 引用了剧情图外前置关卡");
            }
            if (StaminaCost < 0 || MaxExp < 0 || MaxSoftCoin < 0 || MaxHardCoin < 0 || MaxDropCount is < 0 or > MaximumClientDropCount || NumRareDrops is < 0 or > 5)
            {
                throw new InvalidDataException($"普通剧情关卡 {ID} 的消耗、收益或掉落数量非法");
            }
            if (FirstClearRewards.Count > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"普通剧情关卡 {ID} 的首通奖励数量超过 140 上限 {byte.MaxValue}");
            }

            for (int index = 0; index < RareDrops.Count; index++)
            {
                StoryRareDropDefinition drop = RareDrops[index];
                if (drop.Id <= 0 || drop.Level < 0 || drop.Chance is < 0 or > PlannedDropChanceScale)
                {
                    throw new InvalidDataException($"关卡 {ID} 的第 {index + 1} 个掉落槽非法");
                }
                if (drop.Type is < 0 or > byte.MaxValue || drop.Id > ushort.MaxValue || drop.Level > byte.MaxValue)
                {
                    throw new InvalidDataException($"关卡 {ID} 的掉落槽超出 65 字段范围");
                }

                switch (drop.Type)
                {
                    case 1:
                    case 2:
                    case 3:
                        var equipment = EquipmentTableBase.Find(checked((byte)drop.Type), drop.Id)
                            ?? throw new InvalidDataException($"关卡 {ID} 的掉落装备不存在");
                        if (drop.Level is < 1 || drop.Level > equipment.MaxLv)
                        {
                            throw new InvalidDataException($"关卡 {ID} 的掉落装备等级非法");
                        }
                        break;
                    case 10:
                        if (GameTableCatalog.Instance.GetDataById<StackMaterialData>(drop.Id) is null || drop.Level != 1)
                        {
                            throw new InvalidDataException($"关卡 {ID} 的掉落材料不存在或等级非法");
                        }
                        break;
                    case 30:
                        // 客户端将类型 30 作为 RewardItem.AwardType.GeneralCoin，掉落 ID 直接
                        // 对应 equipType/coinType；该类型不要求 RewardData 中存在展示行
                        // （当前章节使用 30415 这一服务端货币类型）。
                        if (drop.Level != 1 || drop.Id <= 0 || drop.Id > short.MaxValue)
                        {
                            throw new InvalidDataException($"关卡 {ID} 的通用货币掉落类型或 ID 非法");
                        }
                        break;
                    default:
                        throw new InvalidDataException($"关卡 {ID} 包含未知计划掉落类型 {drop.Type}");
                }
            }
        }

        if (IsRolePlay)
        {
            if (StaminaCost < 0 || MaxExp < 0 || MaxSoftCoin < 0 || MaxHardCoin < 0 || MaxDropCount is < 0 or > MaximumClientDropCount)
            {
                throw new InvalidDataException($"RP 关卡 {ID} 的消耗或收益上限非法");
            }
            foreach (int parent in RolePlayParentIds)
            {
                if (GameTableCatalog.Instance.GetDataById<LevelMetaV2>(parent) is null)
                {
                    throw new InvalidDataException($"RP 关卡 {ID} 引用了不存在的前置关卡 {parent}");
                }
            }
        }

        if (IsNewMainStory && (StaminaCost < 0 || MaxExp < 0 || MaxSoftCoin < 0 || MaxHardCoin < 0 || MaxDropCount is < 0 or > MaximumClientDropCount))
        {
            throw new InvalidDataException($"崩坏学园篇关卡 {ID} 的消耗或收益上限非法");
        }

        if (ID != StoryRootLevelId)
        {
            return;
        }

        // ---- 根行哨兵执行一次图级校验：关卡数量上限与两张图的无环性 ----
        IReadOnlyList<LevelMetaV2> all = GameTableCatalog.Instance.GetAllData<LevelMetaV2>();
        LevelMetaV2[] storyLevels = [.. all.Where(value => value.IsOrdinaryStory)];
        if (storyLevels.Length > MaximumStoryLevelCount)
        {
            throw new InvalidDataException(
                $"普通剧情关卡数量 {storyLevels.Length} 超过 140 上限 {MaximumStoryLevelCount}");
        }

        Dictionary<int, byte> storyStates = [];
        Dictionary<int, LevelMetaV2> storyById = storyLevels.ToDictionary(value => value.ID);
        foreach (LevelMetaV2 level in storyLevels)
        {
            VisitStory(level.ID);
        }

        void VisitStory(int levelId)
        {
            if (storyStates.GetValueOrDefault(levelId) == 2)
            {
                return;
            }
            if (storyStates.GetValueOrDefault(levelId) == 1)
            {
                throw new InvalidDataException($"普通剧情关卡图存在循环，涉及关卡 {levelId}");
            }

            storyStates[levelId] = 1;
            foreach (int parent in storyById[levelId].StoryParentIds)
            {
                if (storyById.ContainsKey(parent))
                {
                    VisitStory(parent);
                }
            }
            storyStates[levelId] = 2;
        }

        Dictionary<int, byte> rolePlayStates = [];
        Dictionary<int, LevelMetaV2> rolePlayById = all.Where(value => value.IsRolePlay)
            .ToDictionary(value => value.ID);
        foreach (LevelMetaV2 level in rolePlayById.Values)
        {
            VisitRolePlay(level.ID);
        }

        void VisitRolePlay(int levelId)
        {
            if (rolePlayStates.GetValueOrDefault(levelId) == 2)
            {
                return;
            }
            if (rolePlayStates.GetValueOrDefault(levelId) == 1)
            {
                throw new InvalidDataException($"RP 关卡图存在循环，涉及关卡 {levelId}");
            }

            rolePlayStates[levelId] = 1;
            foreach (int parent in rolePlayById[levelId].RolePlayParentIds)
            {
                if (rolePlayById.ContainsKey(parent))
                {
                    VisitRolePlay(parent);
                }
            }
            rolePlayStates[levelId] = 2;
        }
    }

    private static bool HasDropData(StoryRareDropDefinition drop) =>
        drop.Type != 0 || drop.Id != 0 || drop.Level != 0 || drop.Chance != 0;
}

public sealed class LevelChooseLocalData : TableBase
{
    public const string TablePath = "Data/LevelChooseV2";

    public int ID { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Title { get; set; }

    public int Description { get; set; }

    public string ResourceTag { get; set; } = string.Empty;

    public bool HoldResource { get; set; }

    [Name("stageNumber")]
    public short StageNumber { get; set; }

    public int Dialogue { get; set; }

    public bool HideEntry { get; set; }

    public override int GetId() => ID;
}

public sealed class KyusyoData : TableBase
{
    public const string TablePath = "Data/KyusyoData";
    public int CharLevel { get; set; }
    public override int GetId() => CharLevel;
    public int ExpNeeded { get; set; }
    public int MaxStamina { get; set; }
    public float Speed { get; set; }
    public float HP { get; set; }
    public float MaxEnergy { get; set; }
    public bool Weapon1Unlock { get; set; }
    public float Weapon1Attack { get; set; }
    public float Weapon1Crit { get; set; }
    public float Weapon1CritDamage { get; set; }
    public int Weapon1Slots { get; set; }
    public bool Weapon2Unlock { get; set; }
    public float Weapon2Attack { get; set; }
    public float Weapon2Crit { get; set; }
    public float Weapon2CritDamage { get; set; }
    public int Weapon2Slots { get; set; }
    public bool Weapon3Unlock { get; set; }
    public float Weapon3Attack { get; set; }
    public float Weapon3Crit { get; set; }
    public float Weapon3CritDamage { get; set; }
    public int Weapon3Slots { get; set; }
    public bool Ultimate4Unlock { get; set; }
    public int UltimateSlots5 { get; set; }
    public int UltimateSlots6 { get; set; }
    public int UltimateSlots7 { get; set; }

    public override void Verification()
    {
        if (CharLevel == 1 && (ExpNeeded <= 0 || MaxStamina <= 0))
        {
            throw new InvalidDataException("KyusyoData 等级 1 的经验或体力上限非法");
        }
    }
}

public sealed class KyusyoLevelMeta : TableBase
{
    public const string TablePath = "Data/KyusyoLevelMeta";
    /// <summary>初始解锁的普通关卡必须只有一个可消耗体力进入，其余为剧情/迷宫等免体力入口。</summary>
    public const int OrdinaryType = 1;
    public const int MazeType = 3;
    public int ID { get; set; }
    public override int GetId() => ID;
    public int Type { get; set; }
    public bool InitUnlock { get; set; }
    public int Progress { get; set; }
    public List<int> ParentId { get; set; } = [];
    // 名称 TextMap 引用（"TEXT\d+" 形式），GM Handbook 用它生成「关卡ID↔名称」映射
    public string Title { get; set; } = string.Empty;
    public int KyusyoStaminaCost { get; set; }
    public int LevelTypeForServer { get; set; }
    public int RecommendedLevel { get; set; }
    public int MaxExp { get; set; }
    public int MaxSoftCoin { get; set; }
    public int MaxDropCount { get; set; }
    public int NumRareDrops { get; set; }
    public int RareDrop1Type { get; set; }
    public int RareDrop1ID { get; set; }
    public int RareDrop1Level { get; set; }
    public int RareDrop1Chance { get; set; }
    public int RareDrop2Type { get; set; }
    public int RareDrop2ID { get; set; }
    public int RareDrop2Level { get; set; }
    public int RareDrop2Chance { get; set; }

    public override void Verification()
    {
        foreach (int parentId in ParentId.Where(value => value != 0))
        {
            if (GameTableCatalog.Instance.GetDataById<KyusyoLevelMeta>(parentId) is null)
            {
                throw new InvalidDataException($"九霄关卡 {ID} 引用了不存在的前置关卡 {parentId}");
            }
        }

        if (!ReferenceEquals(GameTableCatalog.Instance.GetAllData<KyusyoLevelMeta>()[0], this))
        {
            return;
        }

        // 哨兵行执行一次表级校验：初始关卡集合与默认武器覆盖必须与当前客户端资源一致。
        int[] initialLevels = GameTableCatalog.Instance.GetAllData<KyusyoLevelMeta>()
            .Where(value => value.InitUnlock)
            .OrderBy(value => value.ID)
            .Select(value => value.ID)
            .ToArray();
        if (!initialLevels.SequenceEqual([1, 951, 952, 953, 954, 986, 991, 992, 993, 994]))
        {
            throw new InvalidDataException("KyusyoLevelMeta 初始关卡集合与当前客户端资源不匹配");
        }

        KyusyoLevelMeta firstLevel = GameTableCatalog.Instance.GetDataById<KyusyoLevelMeta>(1)
            ?? throw new InvalidDataException("KyusyoLevelMeta 缺少首关 1");
        if (firstLevel.Type == MazeType || firstLevel.KyusyoStaminaCost <= 0)
        {
            throw new InvalidDataException("九霄首关必须是可消耗体力进入的普通关卡");
        }
        if (GameTableCatalog.Instance.GetAllData<KyusyoLevelMeta>()
                .Where(value => value.InitUnlock && value.Type == OrdinaryType)
                .Count(value => value.KyusyoStaminaCost > 0) != 1)
        {
            throw new InvalidDataException("九霄初始普通关卡必须只有一个可消耗体力进入的关卡");
        }

        int[] defaultWeaponTypes = GameTableCatalog.Instance.GetAllData<KyusyoWeaponData>()
            .Where(value => value.IsDefault)
            .OrderBy(value => value.WeaponType)
            .Select(value => value.WeaponType)
            .ToArray();
        if (!defaultWeaponTypes.SequenceEqual(Enumerable.Range(0, 8)))
        {
            throw new InvalidDataException("KyusyoWeaponData 默认武器类型必须完整覆盖 0-7");
        }
    }
}

public sealed class KyusyoMissionData : TableBase
{
    public const string TablePath = "Data/KyusyoMissionData";
    public int MissionID { get; set; }
    public override int GetId() => MissionID;
    public List<int> ParentID { get; set; } = [];
    // 任务名文本 ID（TextMap），GM Handbook 的任务目录用它生成「任务ID↔名称」映射
    public int NameText { get; set; }
    public int ShowType { get; set; }
    public int SubType { get; set; }
    public int Progress { get; set; }
    public int StoryIDStart { get; set; }
    public int StoryIDEnd { get; set; }
    public List<int> LevelBond { get; set; } = [];
    public List<int> LevelUnlock { get; set; } = [];
    // 接受主线任务时解锁的探索（ExpoUnlock 列）；官服在 MissionStart 后以 AvatarExpoSync 下发
    public List<int> ExpoUnlock { get; set; } = [];
    // 领取主线任务奖励时解锁的九霄事项（KyosyoUnlock 列）
    public List<int> KyosyoUnlock { get; set; } = [];
    public int RewardItemID1 { get; set; }
    public int RewardItemID2 { get; set; }
    public int RewardItemID3 { get; set; }
    public int RewardItemID4 { get; set; }
    public int RewardItemID5 { get; set; }

    public override void Verification()
    {
        foreach (int rewardId in new[]
                 {
                     RewardItemID1, RewardItemID2, RewardItemID3, RewardItemID4, RewardItemID5,
                 }.Where(value => value != 0))
        {
            if (GameTableCatalog.Instance.GetDataById<RewardItemData>(rewardId) is null)
            {
                throw new InvalidDataException($"九霄任务 {MissionID} 引用了不存在的奖励 {rewardId}");
            }
        }

        // 哨兵行执行一次表级校验：根主线任务有且仅有一个（MissionID=2，无真实父任务）。
        if (ReferenceEquals(GameTableCatalog.Instance.GetAllData<KyusyoMissionData>()[0], this))
        {
            KyusyoMissionData[] rootMissions = GameTableCatalog.Instance.GetAllData<KyusyoMissionData>()
                .Where(value => value.ShowType == 1 && value.ParentID.All(parent => parent == 0))
                .ToArray();
            if (rootMissions.Length != 1 || rootMissions[0].MissionID != 2)
            {
                throw new InvalidDataException("KyusyoMissionData 必须有且仅有根主线任务 2");
            }
        }
    }
}

public sealed class KyusyoExpoData : TableBase
{
    public const string TablePath = "Data/KyusyoExpoData";
    public int ExpoID { get; set; }
    public override int GetId() => ExpoID;
    // 本地化名称 TextMap ID（HandBook 成就目录用），TSV 头列已存在
    public int NameText { get; set; }
    public int ExpoType { get; set; }
    public int MaxProgress { get; set; }
    public int HardCoin { get; set; }
    public int SoftCoin { get; set; }
    public int KyosyoExp { get; set; }
}

public sealed class KyusyoMazeTreasureData : TableBase
{
    public const string TablePath = "Data/KyusyoMazeTreasureData";
    public int TreasureID { get; set; }
    public int LevelID { get; set; }
    public int RewardID { get; set; }

    public override int GetId() => checked(LevelID * 100000 + TreasureID);
}

public sealed class KyusyoMazeSubLevelData : TableBase
{
    public const string TablePath = "Data/KyusyoMazeSubLevel";
    public int LevelID { get; set; }
    public int SubLevelID { get; set; }
    public List<int> Treasure { get; set; } = [];
    public List<int> MemoryPieces { get; set; } = [];
    public int Staminia { get; set; }
    public int MaxExp { get; set; }
    public int MaxSoftCoin { get; set; }

    public override int GetId() => LevelID * 10000 + SubLevelID;

}

// KyusyoMazeTriggerUnlockData.tsv 列：TriggerID, LevelID, PortalID (List<int>), ExploreTierRate
// 客户端 MoleMole.MazeGeneralLogic.AdjustMazeLevelSubmitInfo（IDA 0x08C04B93）按 trigger_info
// 重建 _TriggeredTrigger / _TriggeredPortalDic。服务端 LevelBeginRsp.maze_detail_info.trigger_info
// 必填此项；触发器 ID → 传送门 ID 列表，存于服务端迷宫表（表驱动）。
public sealed class KyusyoMazeTriggerUnlockData : TableBase
{
    public const string TablePath = "Data/KyusyoMazeTriggerUnlockData";
    public int TriggerID { get; set; }
    public int LevelID { get; set; }
    public List<int> PortalID { get; set; } = [];
    public int ExploreTierRate { get; set; }

    public override int GetId() => TriggerID;
}

public sealed class KyusyoWeaponData : TableBase
{
    public const string TablePath = "Data/KyusyoWeaponData";
    public int WeaponID { get; set; }
    public int WeaponType { get; set; }
    public bool IsDefault { get; set; }
    public float SpeedRatio { get; set; }

    public override int GetId() => WeaponID;
}

/// <summary>
/// 客户端 MoleMole.LevelChooseMainStoryEntranceMetaData 对应的主线入口表项。
/// </summary>
public sealed class LevelChooseMainStoryEntranceMetaData : TableToolsTableBase
{
    public const string TablePath = "Data/LevelChooseMainStoryMeta";

    public int iD { get; set; }

    public int levelChooseID { get; set; }

    public int textID { get; set; }

    public string textColor { get; set; } = string.Empty;

    public int titleTextImgIndex { get; set; }

    public int starTextType { get; set; }

    public int unLockTipTextID { get; set; }

    public override int GetId() => iD;

    public override void Verification()
    {
        if (!ReferenceEquals(GameTableCatalog.Instance.GetAllData<LevelChooseMainStoryEntranceMetaData>()[0], this))
        {
            return;
        }

        int[] mainStoryEntranceIds = GameTableCatalog.Instance.GetAllData<LevelChooseMainStoryEntranceMetaData>().OrderBy(value => value.iD).Select(value => value.levelChooseID).ToArray();
        if (!mainStoryEntranceIds.SequenceEqual([1, 200, 201, 202, 203, 204]))
        {
            throw new InvalidDataException("LevelChooseMainStoryMeta 的主线入口配置与客户端版本不匹配");
        }

        foreach (int levelChooseId in mainStoryEntranceIds)
        {
            if (GameTableCatalog.Instance.GetDataById<LevelChooseLocalData>(levelChooseId) is null)
            {
                throw new InvalidDataException($"LevelChooseV2 缺少主线入口 {levelChooseId}");
            }
        }
    }
}

