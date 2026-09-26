using System.Globalization;
using System.Text.Json;
using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class HeroMgrLogic(Player player) : PlayerLogicBase(player)
{
    private static readonly string[] ArtifactAttributes = ["_base_max_hp", "_base_phy_att_str", "_base_mag_att_str", "_base_phy_def", "_base_mag_def"];

    /// <summary>客户端 const.ARTIFACT_OPEN_LEVEL，普通神器操作的账号等级门槛。</summary>
    public const int ArtifactOpenLevel = 30;

    /// <summary>客户端 const.HERO_AWAKE_MAX_STAGE。</summary>
    public const int MaxAwakeStage = 4;

    public const int MaxStarLevel = 4;
    public const int MaxStarOrder = 4;

    /// <summary>异界体比普通角色多一档星级上限。</summary>
    public const int YjtMaxStarLevel = 5;

    /// <summary>新获得角色的影装 COST 上限，与客户端登录快照默认值一致。</summary>
    private const int DefaultTreasureCost = 25;

    private HeroMgrComp Comp => Player.SaveData.HeroMgrComp;

    public int[] HeroIds => Comp.Heroes.Select(hero => hero.HeroId).ToArray();
    public int[] AvailableHeroIds => HeroIds.Where(id => !Comp.BannedHeroes.ContainsKey(id) && !Comp.CineLockedHeroes.Contains(id)).ToArray();
    public int[] CineLocked => Comp.CineLockedHeroes.ToArray();

    public HeroState? Find(int heroId) => Comp.Heroes.FirstOrDefault(hero => hero.HeroId == heroId);

    public bool ValidateTeam(int[] heroes, int fatigue = 0, int min = 1, int max = 3)
    {
        return heroes.Length >= min && heroes.Length <= max && heroes.Distinct().Count() == heroes.Length &&
            heroes.All(id => Find(id) is { } hero && hero.Fatigue >= fatigue && !Comp.BannedHeroes.ContainsKey(id) && !Comp.CineLockedHeroes.Contains(id));
    }

    public void ConsumeTeamFatigue(int[] heroes, int amount)
    {
        if (amount < 0 || !ValidateTeam(heroes, amount)) throw new InvalidOperationException("神器使队伍或疲劳无效");
        foreach (int hero in heroes) ConsumeFatigue(hero, amount);
        Synchronize();
    }

    /// <summary>
    /// 增量同步。客户端 syncHeroesData 用 d.iteritems() 遍历，要求 d 是
    /// {heroId: snapshot} 字典，与登录快照 hrs 的数组格式不同，不能复用 ToSnapshot。
    /// 单个 HeroSnapshot 约 600 字节，140 名全量约 86KB，逼近 128KB 帧上限，
    /// 因此按批分片推送：客户端逐包 initFromDict，分片与整体等价。
    /// </summary>
    public void Synchronize()
    {
        const int batchSize = 40;
        foreach (HeroState[] batch in Comp.Heroes.Chunk(batchSize))
        {
            Dictionary<string, object> updates = new();
            foreach (HeroState hero in batch) updates[hero.HeroId.ToString(CultureInfo.InvariantCulture)] = HeroSnapshot(hero);
            Notify("syncHeroesData", new() { ["d"] = updates });
        }
    }

    public void SetBanned(int id, int reason)
    {
        bool wasBanned = Comp.BannedHeroes.ContainsKey(id);
        if (reason == 0) Comp.BannedHeroes.Remove(id);
        else Comp.BannedHeroes[id] = reason;
        if (wasBanned != (reason != 0) && Find(id) is not null)
            Notify(reason == 0 ? "recoverBanHeroes" : "banHeroes", new() { [reason == 0 ? "rhs" : "bhs"] = new[] { id } });
        MarkDirty();
    }

    public void LockCinematic(int[] heroes)
    {
        int[] added = heroes.Where(id => !Comp.CineLockedHeroes.Contains(id)).Distinct().ToArray();
        Comp.CineLockedHeroes.Add(added);
        if (added.Length > 0) Notify("cineLockHeroes", new() { ["h"] = added });
        MarkDirty();
    }

    public Dictionary<int, int> Banned() => Comp.BannedHeroes.ToDictionary(pair => pair.Key, pair => pair.Value);

    public void ResetStoryState()
    {
        // 神器使所有权与全部养成数据都是长期资产，不能随主线重置删除或回退:
        // 星级、神器等级、觉醒、解放、影装、皮肤、挂饰一律保留在原对象上。
        // 这里只重置周期状态: 疲劳、好感、本周攻略、剧情禁用与剧情锁。
        foreach (HeroState hero in Comp.Heroes)
        {
            hero.Fatigue = GameTableCatalog.Instance.GetDataById<HeroData>(hero.HeroId)?.FatigueValue ?? 100;
            hero.Friendly = 0;
            hero.ConqueredThisWeek = false;
        }
        Comp.BannedHeroes.Clear();
        Comp.CineLockedHeroes.Clear();
        MarkDirty();
    }

    public object[] ToSnapshot(bool banned = false)
    {
        return Comp.Heroes.Where(hero => Comp.BannedHeroes.ContainsKey(hero.HeroId) == banned).Select(hero => (object)new object[]
        {
            hero.HeroId,
            HeroSnapshot(hero),
        }).ToArray();
    }

    /// <summary>
    /// 构建单个神器使的完整客户端结构。字段集合必须与客户端 HeroData.initFromDict 对齐:
    /// 缺少必填字段会抛异常，多余或遗漏的可选字段会静默清空对应客户端状态。
    /// </summary>
    public Dictionary<string, object?> HeroSnapshot(HeroState hero)
    {
        GameTableCatalog.Instance.TryGetDataById<HeroData>(hero.HeroId, out HeroData? row);
        int artifactLevel = Math.Max(hero.ArtifactLevel, 1);

        var artifactAttrs = new Dictionary<string, object>();
        var artifactThresholds = new Dictionary<string, object>();
        var artifactRandCounts = new Dictionary<string, object>();
        foreach (string attribute in ArtifactAttributes)
        {
            artifactAttrs[attribute] = hero.ArtifactAttrs.GetValueOrDefault(attribute);
            artifactThresholds[attribute] = GetArtifactThreshold(attribute, artifactLevel);
            artifactRandCounts[attribute] = 0;
        }

        return new Dictionary<string, object?>
        {
            ["sl"] = hero.StarLevel,
            ["so"] = hero.StarOrder,
            ["cf"] = hero.Fatigue,
            ["ef"] = hero.Friendly,
            // oa 是根节点的布尔开关，决定神器是否已开启；不要与 ar.oa 的属性点混淆。
            ["oa"] = hero.OpenArtifact,
            ["ar"] = new Dictionary<string, object>
            {
                ["o"] = artifactLevel,
                ["ss"] = 3,
                ["sl"] = Array.Empty<object>(),
                ["oa"] = artifactAttrs,
                ["ot"] = artifactThresholds,
                ["rotc"] = artifactRandCounts,
                ["fn"] = 0,
                ["aao"] = hero.AwakeStage * 5,
                ["il"] = hero.ArtifactIncLevel,
            },
            ["ti"] = TreasureSnapshot(hero),
            ["cat"] = null,
            ["sr"] = CalculateScore(hero),
            ["fd"] = hero.Conquered,
            ["cfd"] = hero.ConqueredThisWeek,
            ["as"] = hero.AwakeStage,
            ["ass"] = hero.AwakeSkinStyle,
            ["ls"] = hero.LiberateStage,
            ["la"] = hero.LiberateExp.GetValueOrDefault("la"),
            ["lh"] = hero.LiberateExp.GetValueOrDefault("lh"),
            ["ld"] = hero.LiberateExp.GetValueOrDefault("ld"),
            ["sk"] = BuildSkinDict(hero),
            ["cs"] = string.IsNullOrEmpty(hero.CurrentSkinUuid) ? null : hero.CurrentSkinUuid,
            ["pa"] = Array.Empty<object>(),
            ["wpa"] = hero.PendantUuids.ToArray(),
            ["pat"] = Array.Empty<object>(),
            ["jbt"] = hero.BondTreasureUuids.Select(uuid => (object?)uuid).Concat([null]).Take(2).ToArray(),
            ["at"] = hero.ObtainTime,
        };
    }

    /// <summary>
    /// 已装备影装列表。客户端 InvData.initFromDict 调用的是 updateItems 而不是整体替换，
    /// 因此这里下发真实存量；卸装必须走显式 RPC，不能靠下发空列表。
    /// </summary>
    private Dictionary<string, object> TreasureSnapshot(HeroState hero)
    {
        var items = new List<object>();
        foreach (string uuid in hero.EquippedTreasures.Keys)
        {
            TreasureInstance? treasure = Comp.Treasures.FirstOrDefault(entry => entry.Uuid == uuid);
            if (treasure is null) continue;
            items.Add(new Dictionary<string, object>
            {
                ["u"] = uuid,
                ["i"] = treasure.ItemId,
                ["w"] = treasure.Level,
                ["ex"] = treasure.Exclusive,
                ["exp"] = new Dictionary<string, object> { ["stage"] = treasure.ExclusiveStage },
            });
        }

        return new Dictionary<string, object>
        {
            ["mc"] = hero.TreasureMaxCost > 0 ? hero.TreasureMaxCost : DefaultTreasureCost,
            ["items"] = items.ToArray(),
        };
    }

    /// <summary>
    /// 皮肤字典。客户端 HeroData.initFromDict 会对 sk 的每个值调用 SkinData.initFromDict，
    /// 该方法直接索引 i(皮肤 ID) 与 u(UUID)，任何一个缺失都会抛 KeyError 中断登录，
    /// 因此这里必须下发完整结构，不能只给 UUID。找不到实例时回落到默认皮肤，
    /// 与客户端 getHeroSkinData 对未拥有皮肤返回伪造数据的做法一致。
    /// </summary>
    private Dictionary<string, object> BuildSkinDict(HeroState hero)
    {
        var skins = new Dictionary<string, object>();
        foreach (string uuid in hero.SkinUuids)
        {
            SkinInstance? skin = Comp.Skins.FirstOrDefault(entry => entry.Uuid == uuid);
            if (skin is null) continue;
            skins[uuid] = new Dictionary<string, object>
            {
                ["i"] = skin.SkinId,
                ["u"] = skin.Uuid,
                ["cd"] = skin.CurDye,
                ["pd"] = skin.PendingDye,
                ["cr"] = skin.CreateTime,
                ["dst"] = skin.DyeSetTime,
            };
        }

        return skins;
    }

    /// <summary>属性阈值随神器等级成长，这里取每级提供的点数作为阈值基线。</summary>
    private static int GetArtifactThreshold(string attribute, int artifactLevel) =>
        GameTableCatalog.Instance.TryGetDataById<ArtifactAttrLevelData>(artifactLevel, out ArtifactAttrLevelData? row)
            ? row.Point
            : 0;

    public bool Unlock(int heroId)
    {
        if (!GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row))
        {
            throw new ArgumentOutOfRangeException(nameof(heroId), "神器使 ID 不存在");
        }
        if (Comp.Heroes.Any(hero => hero.HeroId == heroId)) return false;

        Comp.Heroes.Add(new HeroState
        {
            HeroId = heroId,
            StarLevel = 1,
            StarOrder = 1,
            Fatigue = row.FatigueValue,
            // 神器默认关闭: 满星后需要一次显式开启，不能在获得角色时直接打开。
            OpenArtifact = false,
            ArtifactLevel = 1,
            TreasureMaxCost = DefaultTreasureCost,
            ObtainTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        MarkDirty();
        object[] snapshot = ToSnapshot(Comp.BannedHeroes.ContainsKey(heroId)).Cast<object[]>().First(value => (int)value[0] == heroId);
        Notify("addHero", new() { ["h"] = heroId, ["d"] = snapshot[1] });
        if (Comp.BannedHeroes.ContainsKey(heroId)) Notify("banHeroes", new() { ["bhs"] = new[] { heroId } });
        return true;
    }

    public int UnlockAll()
    {
        int count = 0;
        foreach (HeroData row in GameTableCatalog.Instance.GetAllData<HeroData>())
        {
            if (Unlock(row.ProtoId)) count++;
        }
        return count;
    }

    /// <summary>
    /// 升星或开启神器。客户端在 artifact=false 时会自行调用 incStarOrder() 自增星级，
    /// 因此这里只推进服务端存档并返回是否成功，不要再下发已自增的完整快照。
    /// 满星但尚未开启神器时，同一个请求走开启分支，artifact 回复为 true。
    /// </summary>
    public bool IncStarOrder(int heroId, out bool openedArtifact)
    {
        openedArtifact = false;
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is null) return false;

        if (!GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row)) return false;
        int maxStarLevel = IsYijieti(heroId) ? YjtMaxStarLevel : MaxStarLevel;

        // 满星且已开启神器才是真正无路可走；满星未开启要在这里放行开启流程。
        if (hero.StarLevel >= maxStarLevel && hero.StarOrder >= MaxStarOrder)
        {
            if (hero.OpenArtifact) return false;
            hero.OpenArtifact = true;
            openedArtifact = true;
            MarkDirty();
            return true;
        }

        int nextLevel = hero.StarOrder >= MaxStarOrder ? hero.StarLevel + 1 : hero.StarLevel;
        int nextOrder = hero.StarOrder >= MaxStarOrder ? 1 : hero.StarOrder + 1;
        if (nextLevel > maxStarLevel || nextOrder > MaxStarOrder) return false;

        if (!GameTableCatalog.Instance.TryGetDataById<HeroStarData>(
                (nextLevel * 10) + nextOrder, out HeroStarData? starCost) || starCost.Fragment <= 0)
        {
            return false;
        }

        // 升星消耗角色碎片。碎片属于背包资产，必须在玩家锁内经 InventoryLogic 扣除。
        // 异界体消耗 yjt_fragment，不在普通碎片表里；查不到对应道具时直接失败，不能白送星级。
        if (!TryConsumeStarFragment(heroId, starCost.Fragment, maxStarLevel > MaxStarLevel)) return false;

        hero.StarLevel = nextLevel;
        hero.StarOrder = nextOrder;
        MarkDirty();
        return true;
    }

    /// <summary>
    /// 扣除升星所需碎片。普通角色用 fragment_item 的碎片物品；
    /// 异界体在 fragment_item 中没有条目，其碎片属于另一套 type=22/23 的道具，尚未接入背包，
    /// 因此异界体升星当前只能通过 /hero 直设星级完成。
    /// </summary>
    private bool TryConsumeStarFragment(int heroId, int amount, bool isYijieti)
    {
        if (isYijieti) return false;
        if (!GameTableCatalog.Instance.TryGetDataById<HeroFragmentItemData>(heroId, out HeroFragmentItemData? fragment)) return false;
        return Player.Inventory.Consume(fragment.ItemId, amount);
    }

    /// <summary>
    /// 异界体判定，直接读 hero.json 的 yijieti 字段，与客户端 HeroData.yijieti 同源。
    /// 当前资源里只有 122 安托涅瓦、125 幽桐、139 零、145 璃璃子 4 名异界体，
    /// 它们的升星与神器消耗走 yjt_fragment，不在普通碎片表里。
    /// </summary>
    public static bool IsYijieti(int heroId) =>
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row) && row.Yijieti != 0;

    /// <summary>
    /// 神器升级。客户端收到回复后会自行加一级并累加 COST，
    /// 因此回复里只给增量信息，不要下发升级后的完整快照。
    /// </summary>
    public bool IncArtifactLevel(int heroId, out int nextLevel, out int nextCost)
    {
        nextLevel = 0;
        nextCost = 0;
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is null || !hero.OpenArtifact) return false;
        if (Player.Profile.Level <= ArtifactOpenLevel) return false;

        int target = hero.ArtifactLevel + 1;
        if (!GameTableCatalog.Instance.TryGetDataById<ArtifactLevelData>(target, out ArtifactLevelData? cost)) return false;
        if (cost.Fragment <= 0) return false;

        // 神器升级同样消耗角色碎片；缺失碎片道具时直接失败。
        if (!GameTableCatalog.Instance.TryGetDataById<HeroFragmentItemData>(heroId, out HeroFragmentItemData? fragment) ||
            !Player.Inventory.Consume(fragment.ItemId, cost.Fragment))
        {
            return false;
        }
        hero.ArtifactLevel = target;
        nextLevel = target;
        nextCost = cost.Fragment;
        MarkDirty();
        return true;
    }

    /// <summary>神器等级上限由账号等级与觉醒共同决定，与客户端面板保持一致。</summary>
    public int GetArtifactLevelCap(HeroState hero)
    {
        int cap = Player.Profile.Level - ArtifactOpenLevel + 1 + hero.AwakeStage * 5;
        return Math.Max(1, cap);
    }

    /// <summary>觉醒突破。需要已开启神器、满足神器等级门槛，并消耗觉醒材料。</summary>
    public bool UpgradeAwakeStage(int heroId)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is null || !hero.OpenArtifact || hero.AwakeStage >= MaxAwakeStage) return false;
        if (!GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row) || row.OpenAwake == 0) return false;

        int nextStage = hero.AwakeStage + 1;
        if (!GameTableCatalog.Instance.TryGetDataById<AwakeConsumeData>(
                (heroId * 100) + nextStage, out AwakeConsumeData? cost)) return false;
        if (hero.ArtifactLevel < cost.ArtifactLevelUp) return false;

        // 首次突破与后续每级消耗的是不同物品，取表里配置的对应字段。
        int itemId = hero.AwakeStage == 0 ? cost.PriorConsumeItem : cost.UpdateConsumeItem;
        int itemCount = hero.AwakeStage == 0 ? 1 : Math.Max(cost.UpdateConsumeNum, 1);
        if (itemId > 0 && !Player.Inventory.Consume(itemId, itemCount)) return false;

        hero.AwakeStage = nextStage;
        MarkDirty();
        return true;
    }

    public int GetInsightValue(int heroId)
    {
        HeroState hero = Comp.Heroes.First(entry => entry.HeroId == heroId);
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row);
        return (row?.InsightValue ?? 0) + (GetStarSkill(hero)?.InsightBonus ?? 0);
    }

    public int GetConstructValue(int heroId)
    {
        HeroState hero = Comp.Heroes.First(entry => entry.HeroId == heroId);
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row);
        return (row?.ConstructValue ?? 0) + (GetStarSkill(hero)?.ConstructBonus ?? 0);
    }

    public int GetLeadershipValue(int heroId)
    {
        HeroState hero = Comp.Heroes.First(entry => entry.HeroId == heroId);
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row);
        return (row?.LeadershipValue ?? 0) + (GetStarSkill(hero)?.LeadershipBonus ?? 0);
    }

    private static HeroStarSkillData? GetStarSkill(HeroState hero) =>
        GameTableCatalog.Instance.TryGetDataById<HeroStarSkillData>(hero.HeroId * 100 + hero.StarLevel * 10 + hero.StarOrder, out HeroStarSkillData? row) ? row : null;

    public bool ConsumeFatigue(int heroId, int amount)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (amount < 0 || hero is null || hero.Fatigue < amount) return false;
        hero.Fatigue -= amount;
        MarkDirty();
        return true;
    }

    public void AddFriendly(int heroId, int amount)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is not null)
        {
            hero.Friendly = Math.Clamp(hero.Friendly + amount, 0, 100);
            MarkDirty();
        }
    }

    public void RestoreAllFatigue(int amount)
    {
        foreach (HeroState hero in Comp.Heroes)
        {
            int max = GameTableCatalog.Instance.GetDataById<HeroData>(hero.HeroId)?.FatigueValue ?? 100;
            hero.Fatigue = Math.Clamp(hero.Fatigue + amount, 0, max);
        }
        MarkDirty();
        Synchronize();
    }

    public void AddFatigue(int heroId, int amount)
    {
        if (heroId == 0) { RestoreAllFatigue(amount); return; }
        if (Find(heroId) is not { } hero) return;
        int max = GameTableCatalog.Instance.GetDataById<HeroData>(heroId)?.FatigueValue ?? 100;
        hero.Fatigue = Math.Clamp(hero.Fatigue + amount, 0, max);
        MarkDirty();
    }

    private int CalculateScore(HeroState hero)
    {
        int level = Player.Profile.Level;
        int star = (hero.StarLevel - 1) * 4 + hero.StarOrder;
        return (int)(level * 100 + level * 100 * 1.74 * star / 16);
    }

    /// <summary>
    /// GM 养成直设。绕过材料消耗与部分前置校验，但仍按资源上限截断，
    /// 保证不会写出客户端无法渲染的越界状态（例如普通角色的 5/5 星级）。
    /// </summary>
    public void ApplyProgression(int heroId, int? starLevel, int? starOrder, int? artifactLevel, int? awakeStage, int? liberateStage)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is null) return;
        if (!GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row)) return;

        int maxStarLevel = IsYijieti(heroId) ? YjtMaxStarLevel : MaxStarLevel;
        if (starLevel is not null) hero.StarLevel = Math.Clamp(starLevel.Value, 1, maxStarLevel);
        if (starOrder is not null) hero.StarOrder = Math.Clamp(starOrder.Value, 1, MaxStarOrder);
        if (starLevel is not null && starOrder is null && hero.StarOrder > MaxStarOrder) hero.StarOrder = MaxStarOrder;

        // 调整星级时保持层级一致，避免出现 2/0 这类客户端读不出的组合。
        if (hero.StarLevel >= maxStarLevel) hero.StarOrder = Math.Min(hero.StarOrder, MaxStarOrder);
        if (hero.StarOrder is < 1 or > MaxStarOrder) hero.StarOrder = Math.Clamp(hero.StarOrder, 1, MaxStarOrder);

        if (artifactLevel is not null)
        {
            int maxArtifact = GetArtifactTableCap();
            hero.ArtifactLevel = Math.Clamp(artifactLevel.Value, 1, maxArtifact);
            // 提升神器等级即视为开启神器，与客户端开启后的状态保持一致。
            if (hero.ArtifactLevel > 1) hero.OpenArtifact = true;
        }

        if (awakeStage is not null)
        {
            // 未开放觉醒的角色不写入觉醒阶段，保持与客户端 open_awake 判断一致。
            int maxAwake = row.OpenAwake == 0 ? 0 : MaxAwakeStage;
            hero.AwakeStage = Math.Clamp(awakeStage.Value, 0, maxAwake);
        }

        if (liberateStage is not null) hero.LiberateStage = Math.Clamp(liberateStage.Value, 0, GetLiberateCap(heroId));

        MarkDirty();
    }

    /// <summary>神器等级的表内上限，避免 GM 写出资源里不存在的等级。</summary>
    private static int GetArtifactTableCap()
    {
        int max = 1;
        foreach (ArtifactLevelData row in GameTableCatalog.Instance.GetAllData<ArtifactLevelData>()) max = Math.Max(max, row.Level);
        return max;
    }

    /// <summary>
    /// 解放等级上限取 liberate_tupo 中该角色最后一个阶段的 lv_max。
    /// 没有配置的角色按 0 处理，避免写出客户端不认识的解放等级。
    /// </summary>
    public static int GetLiberateCap(int heroId)
    {
        if (!GameTableCatalog.Instance.TryGetTable<LiberateTupoData>(out var table)) return 0;
        int cap = 0;
        foreach (LiberateTupoData row in table.Values)
        {
            if (row.HeroId == heroId) cap = Math.Max(cap, row.LvMax);
        }
        return cap;
    }

    public string DescribeProgression(int heroId)
    {
        HeroState? hero = Find(heroId);
        if (hero is null) return "未解锁";
        return $"星级 {hero.StarLevel}/{hero.StarOrder}，神器 {(hero.OpenArtifact ? $"{hero.ArtifactLevel} 级" : "未开启")}，觉醒 {hero.AwakeStage}，解放 {hero.LiberateStage}";
    }
}
