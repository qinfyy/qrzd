using System.Text.Json;
using Serilog;
using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class CombatLogic(Player player) : PlayerLogicBase(player)
{
    private static readonly ILogger Logger = Log.ForContext<CombatLogic>();
    private CombatComp Comp => Player.SaveData.CombatComp;
    public bool IsActive => Comp.Active is { State: 1 };
    public CombatInstance? Active => Comp.Active;
    public IReadOnlyList<CombatInstance> Settled => Comp.Settled;

    /// <summary>最近一次入场的拒绝原因，由 Enter 写入、CombatHandlers 读取后抛给客户端。</summary>
    public string? LastRejectReason { get; private set; }

    /// <summary>记录一条拒绝原因并返回 null，让调用方能就地 return。</summary>
    private Dictionary<string, object>? RejectBattle(string reason)
    {
        LastRejectReason = reason;
        Logger.Information("City UID={Uid} 战斗拒绝 原因={Reason}", Player.Uid, reason);
        return null;
    }

    protected internal override void OnLogin()
    {
        if (IsActive) ResetBattle();
    }

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["of"] = false, ["ls"] = false, ["sc"] = 0, ["md"] = 0, ["bt"] = 0, ["hr"] = Array.Empty<object>(), ["eh"] = Array.Empty<object>(),
    };

    public Dictionary<string, object>? Enter(int type, int stage, int[] heroes, int areaId = 0)
    {
        // 每一条准入判据都可能成为拒绝原因。集中在一处返回可读原因，避免上层只能报
        // “战斗入场条件不满足”，那种日志等于没有诊断价值。
        // 拒绝原因随异常抛给上层，由 Failure 统一用 showMessageStr 推给客户端。
        // 客户端 combatOfflineReply 的失败分支不显示文本，只能靠这条路径。
        LastRejectReason = null;
        Dictionary<string, object>? Reject(string reason)
        {
            LastRejectReason = reason;
            Logger.Information("City UID={Uid} 战斗拒绝 type={Type} stage={Stage} area={Area} heroes={Heroes} 原因={Reason}",
                Player.Uid, type, stage, areaId, string.Join(',', heroes), reason);
            return null;
        }

        MissionData? mission = GameTableCatalog.Instance.GetDataById<MissionData>(stage);
        if (mission is null) return Reject($"关卡 {stage} 不存在");
        if (!mission.Flag("release")) return Reject($"关卡 {stage} 未发布");
        if (type is not (1 or 2)) return Reject($"战斗类型 {type} 非法");
        if (Player.WeekNum.Week != 0) return Reject("非首周无法战斗");
        if (Player.WeekNum.EndingId != 0) return Reject("结局播放中无法战斗");
        if (IsActive)
        {
            CombatInstance current = Comp.Active;
            return current.Type == type && current.Stage == stage && current.Heroes.SequenceEqual(heroes) ? ReadJson(current.EntryJson) : Reject("已有其他战斗实例未结束");
        }
        int eventId = Player.EventTrigger.EventForBattle(stage);
        bool temporary = type == 2 && mission.PresetHeroes.Length > 0;
        int min = mission.Int("least_hero_num", 1);
        int max = mission.Int("max_hero_num", 3);
        if (heroes.Length < min || heroes.Length > max) return Reject($"队伍人数 {heroes.Length} 不在 {min}..{max}");
        if (heroes.Distinct().Count() != heroes.Length) return Reject("队伍存在重复角色");
        if (temporary && !heroes.SequenceEqual(mission.PresetHeroes)) return Reject("预设助战阵容与服务端不一致");
        if (!temporary && !Player.HeroMgr.ValidateTeam(heroes, 0, min, max)) return Reject("队伍包含未拥有或不可用的神器使");
        if (type == 2 && eventId == 0) return Reject("该战斗没有对应进行中的剧情事件");
        if (type == 1 && mission.Int("system_type") != 1) return Reject($"关卡 {stage} 不是区域战斗");
        if (type == 1 && mission.Area != areaId) return Reject($"关卡 {stage} 属于区域 {mission.Area}，请求区域 {areaId}");
        if (type == 1 && Player.WeekNum.Day >= 7) return Reject("本周目已进入第 7 天");
        if (type == 1 && Player.Status.Emergency) return Reject("紧急状态无法战斗");
        if (type == 1 && (Player.City.FindArea(areaId) is not { Status: 1, Lock: false } area || area.CurrentStage != stage))
            return Reject($"区域 {areaId} 当前关卡不是 {stage}");
        if (type == 1 && !Player.EventTrigger.AllowsStage(stage)) return Reject($"进行中的事件不允许挑战 {stage}");
        if (type == 1 && Player.EventTrigger.Available(false).Any(row => row.Flag("isAutoTriggerEvent"))) return Reject("存在未处理的自动剧情事件");
        if (mission.Int("cond_event") != 0 && !Player.EventTrigger.IsCompleted(mission.Int("cond_event"))) return Reject($"需先完成前置事件 {mission.Int("cond_event")}");
        if (mission.Int("startweeknum") > Player.WeekNum.Week) return Reject("尚未到达开启周目");
        if (mission.Prerequisites.Any(id => !Player.City.PassedStages.Contains(id)))
            return Reject($"前置关卡未通过: {string.Join(',', mission.Prerequisites.Where(id => !Player.City.PassedStages.Contains(id)))}");
        int fatigue = mission.FatigueCost;
        if (type == 1 && Player.City.ForceVal < mission.Int("force_recommend")) fatigue = mission.Int("overFatigue", fatigue);
        int cost = type == 1 ? mission.ActionCost : 0;
        CombatInstance? retry = Comp.Active;
        bool paid = retry is { State: 2 } && retry.Type == type && retry.Stage == stage && retry.Week == Player.WeekNum.Week &&
            retry.Day == Player.WeekNum.Day && retry.Heroes.SequenceEqual(heroes);
        if (!paid && !Player.City.CanGrantActionRewards(cost)) return Reject("行动奖励无法发放，背包可能已满");
        if (!paid && Player.City.ActionVal < cost) return Reject($"行动力不足，剩余 {Player.City.ActionVal}，需要 {cost}");
        if (!paid && !temporary && !Player.HeroMgr.ValidateTeam(heroes, fatigue, min, max)) return Reject($"队伍疲劳不足，本次消耗 {fatigue}");
        if (type == 1 && !paid && Player.City.ActionBlocker(cost) is { } blocked) return Reject(blocked);
        if (heroes.Any(id => GameTableCatalog.Instance.GetDataById<HeroData>(id) is null)) return Reject("队伍包含不存在的神器使");
        int[] rewardIds = BattleRewardIds(mission);
        if (!RewardLogic.CanGrant(Player, rewardIds)) return Reject("战斗奖励无法发放，背包可能已满");
        CityFightRewardData? actionReward = GameTableCatalog.Instance.GetDataById<CityFightRewardData>(Player.Profile.Level);
        if (type == 1 && actionReward is null) return Reject($"等级 {Player.Profile.Level} 没有行动奖励配置");
        if (type == 1 && !RewardLogic.CanGrantRandom(Player, actionReward!.Ints("items"))) return Reject("随机行动奖励无法发放");

        CombatInstance battle = new()
        {
            Id = Guid.NewGuid().ToString("N"), Type = type, Stage = stage, Week = Player.WeekNum.Week, Day = Player.WeekNum.Day,
            EnteredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), State = 1, TemporaryHeroes = temporary, EventId = eventId,
            ActionCost = cost, FatigueCost = fatigue, Area = areaId,
        };
        battle.Heroes.Add(heroes);
        Dictionary<string, object> entry = new()
        {
            ["t"] = type, ["s"] = stage, ["f"] = battle.Id, ["h"] = heroes, ["d"] = new Dictionary<string, object> { ["a"] = areaId },
            ["c"] = CreateCombatData(mission, heroes, temporary),
        };
        battle.EntryJson = JsonSerializer.Serialize(entry);
        if (!paid)
        {
            if (!temporary) Player.HeroMgr.ConsumeTeamFatigue(heroes, fatigue);
            if (cost > 0) Player.City.ConsumeAction(cost);
        }
        Comp.Active = battle;
        Player.Status.CurrentStatus = "battle";
        MarkDirty();
        Logger.Information("City UID={Uid} 战斗入场 {Fight} type={Type} stage={Stage} heroes={Heroes} retry={Retry}", Player.Uid, battle.Id, type, stage, heroes, paid);
        return entry;
    }

    public Dictionary<string, object> CreateCombatData(MissionData mission, int[] heroes, bool temporary)
    {
        return new()
        {
            ["heroes"] = heroes.ToDictionary(id => $"('{Player.Profile.AvatarId}', {id})", id => (object)HeroData(mission, id, temporary)),
            ["hero_lv"] = heroes.Select(id => GameTableCatalog.Instance.GetDataById<MissionHeroData>(MissionHeroData.MakeId(mission.Id, id))?.Int("level") ?? Player.Profile.Level).Max(),
            ["monster_lv"] = mission.Int("monsterLvFirstWeek", 1),
            ["blackcore"] = Player.City.Blackcores().Where(pair => pair.Value == 2).Select(pair => pair.Key).ToArray(),
            ["monster"] = Player.Intelligence.CombatModifiers(),
            ["skins"] = heroes.Select(id => (object)new Dictionary<string, object>
            {
                ["i"] = GameTableCatalog.Instance.GetDataById<MissionHeroData>(MissionHeroData.MakeId(mission.Id, id))?.Int("skin_id", id) ?? id,
                ["u"] = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), ["cd"] = new Dictionary<string, object>(), ["pd"] = new Dictionary<string, object>(), ["dn"] = 0,
            }).ToArray(),
            ["artifact_info"] = heroes.Select(_ => (object)new object[] { false, 0 }).ToArray(), ["awake_stage"] = heroes.Select(_ => 0).ToArray(),
        };
    }

    private Dictionary<string, object> HeroData(MissionData mission, int id, bool temporary)
    {
        Sv.Resources.Tables.HeroData row = GameTableCatalog.Instance.GetDataById<Sv.Resources.Tables.HeroData>(id)!;
        HeroState? owned = temporary ? null : Player.HeroMgr.Find(id);
        MissionHeroData? preset = GameTableCatalog.Instance.GetDataById<MissionHeroData>(MissionHeroData.MakeId(mission.Id, id));
        int star = preset?.Int("starLevel", 1) ?? owned?.StarLevel ?? 1;
        int order = preset?.Int("starOrder", 1) ?? owned?.StarOrder ?? 1;
        int level = preset?.Int("level", Player.Profile.Level) ?? Player.Profile.Level;
        Dictionary<string, double> attributes = new(row.Attrs);
        HeroStarAttributeData? starAttributes = GameTableCatalog.Instance.GetDataById<HeroStarAttributeData>(star * 10000 + order);
        if (starAttributes?.Get("attrs") is { ValueKind: JsonValueKind.Object } starValues)
        {
            var bonuses = starValues.EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetDouble());
            foreach (var pair in new[] { ("max_hp", "hp"), ("phy_att_str", "phy_str"), ("mag_att_str", "mag_str"), ("phy_def", "phy_def"), ("mag_def", "mag_def") })
            {
                double basis = row.Additional.TryGetValue("star_base_" + pair.Item1, out var baseValue) ? baseValue.GetDouble() : 0;
                double factor = row.Additional.TryGetValue("star_factor_base_" + pair.Item1, out var factorValue) ? factorValue.GetDouble() : 0;
                bonuses["_base_" + pair.Item1] = basis + level * bonuses.GetValueOrDefault("_" + pair.Item2 + "_delta") * factor;
                bonuses.Remove("_" + pair.Item2 + "_delta");
            }
            foreach (var bonus in bonuses) attributes[bonus.Key] = attributes.GetValueOrDefault(bonus.Key) + bonus.Value;
        }
        if (preset?.Get("artifact") is { ValueKind: JsonValueKind.Object } artifact && artifact.TryGetProperty("attrs", out var artifactValues))
            foreach (var value in artifactValues.EnumerateObject()) attributes[value.Name] = attributes.GetValueOrDefault(value.Name) + value.Value.GetDouble();
        HeroStarSkillData? skills = GameTableCatalog.Instance.GetDataById<HeroStarSkillData>(id * 100 + star * 10 + order);
        Dictionary<int, int> skillLevels = new(skills?.Skills ?? []);
        if (temporary && mission.Id == 104)
        {
            // The opening battle teaches all three skills before the permanent hero is acquired.
            foreach (int skill in row.ActiveSkills.Append(row.PassiveSkill).Where(skill => skill > 0)) skillLevels.TryAdd(skill, 1);
        }
        object[] starSkills = skillLevels.Select(pair => (object)new[] { pair.Key, pair.Value }).ToArray();
        return new()
        {
            // The client reads this field during local battle settlement and achievement checks.
            ["id"] = id,
            ["d"] = attributes, ["a"] = new Dictionary<string, object>(), ["s"] = new object[] { starSkills, Array.Empty<object>() },
            ["t"] = Array.Empty<object>(), ["ai"] = new object[] { false, 0 }, ["as"] = 0,
        };
    }

    public Dictionary<string, object>? SwitchHero(int type, int stage, int hero)
    {
        if (Comp.Active is not { State: 1, TemporaryHeroes: false } battle || battle.Type != type || battle.Stage != stage)
            return RejectBattle($"没有可换人的战斗（type={type} stage={stage}）");
        if (battle.ExtraHeroes.Count >= 20) return RejectBattle("替补人数已达上限 20");
        if (battle.Heroes.Contains(hero) || battle.ExtraHeroes.Contains(hero)) return RejectBattle($"神器使 {hero} 已在队伍中");
        if (!Player.HeroMgr.ValidateTeam([hero], battle.FatigueCost))
            return RejectBattle($"神器使 {hero} 未拥有、不可用或疲劳不足 {battle.FatigueCost}");
        battle.PendingHero = hero;
        MarkDirty();
        return HeroData(GameTableCatalog.Instance.GetDataById<MissionData>(stage)!, hero, false);
    }

    public bool ConfirmHero(int type, int stage, int hero)
    {
        if (Comp.Active is not { State: 1 } battle || battle.Type != type || battle.Stage != stage)
        {
            RejectBattle($"没有待确认的战斗（type={type} stage={stage}）");
            return false;
        }
        if (battle.ExtraHeroes.Contains(hero)) return true;
        if (battle.PendingHero != hero)
        {
            RejectBattle($"神器使 {hero} 尚未通过换人确认");
            return false;
        }
        if (!Player.HeroMgr.ValidateTeam([hero], battle.FatigueCost))
        {
            RejectBattle($"神器使 {hero} 疲劳不足 {battle.FatigueCost}");
            return false;
        }
        Player.HeroMgr.ConsumeTeamFatigue([hero], battle.FatigueCost);
        battle.ExtraHeroes.Add(hero);
        battle.PendingHero = 0;
        MarkDirty();
        return true;
    }

    public Dictionary<string, object>? Finish(int type, int stage, int[] heroes, int[] extraHeroes, int win)
    {
        if (win is not (0 or 1)) return RejectBattle($"战斗结果 {win} 非法");
        if (heroes.Distinct().Count() != heroes.Length) return RejectBattle("出战列表存在重复角色");
        if (extraHeroes.Distinct().Count() != extraHeroes.Length) return RejectBattle("替补列表存在重复角色");
        CombatInstance? battle = Comp.Active;
        if (battle is null)
        {
            CombatInstance? settled = Comp.Settled.LastOrDefault(value => value.Type == type && value.Stage == stage);
            return settled is not null && settled.Win == win && settled.Heroes.SequenceEqual(heroes) && settled.ExtraHeroes.Order().SequenceEqual(extraHeroes.Order())
                ? RewardReply(settled) : RejectBattle("没有与该结算对应的战斗记录（可能已结算或参数不匹配）");
        }
        if (battle.State != 1 || battle.Type != type || battle.Stage != stage || !battle.Heroes.SequenceEqual(heroes) ||
            !battle.ExtraHeroes.Order().SequenceEqual(extraHeroes.Order()) || battle.Week != Player.WeekNum.Week || battle.Day != Player.WeekNum.Day)
            return RejectBattle("战斗状态或队伍与存档不一致，无法结算");
        MissionData mission = GameTableCatalog.Instance.GetDataById<MissionData>(stage)!;
        if (!RewardLogic.CanGrant(Player, BattleRewardIds(mission))) return RejectBattle("战斗奖励无法发放，背包可能已满");
        Dictionary<string, object> reward = [];
        if (win == 1 && type == 1)
        {
            CityFightRewardData row = GameTableCatalog.Instance.GetDataById<CityFightRewardData>(Player.Profile.Level)!;
            reward = RewardLogic.GrantAction(Player, row, battle.ActionCost);
            RewardLogic.Merge(reward, RewardLogic.GrantRandom(Player, row.Ints("items")));
            RewardLogic.Merge(reward, RewardLogic.Grant(Player, BattleRewardIds(mission)));
            foreach (int hero in heroes.Concat(extraHeroes)) Player.HeroMgr.AddFriendly(hero, row.Int("friendly"));
            Player.HeroMgr.Synchronize();
            Player.City.PassAreaStage(battle.Area, stage);
        }
        battle.Win = win;
        battle.State = 3;
        battle.RewardJson = JsonSerializer.Serialize(reward);
        Comp.Settled.Add(battle);
        Comp.Active = null;
        Player.Status.CurrentStatus = "city";
        Player.EventTrigger.BattleFinished(mission, win == 1);
        Player.City.Synchronize();
        MarkDirty();
        Logger.Information("City UID={Uid} 战斗结算 {Fight} stage={Stage} win={Win}", Player.Uid, battle.Id, stage, win);
        return RewardReply(battle);
    }

    private int[] BattleRewardIds(MissionData mission)
    {
        if (mission.Int("system_type") != 1) return [];
        return GameTableCatalog.Instance.GetDataById<FightFirstWeekData>(mission.Id)?.Ints("reward") ?? [];
    }

    public void ResetBattle()
    {
        if (Comp.Active is not { } active) return;
        active.State = 2;
        active.PendingHero = 0;
        Player.Status.CurrentStatus = "city";
        MarkDirty();
    }

    internal void SkipIntroduction()
    {
        if (Player.Newbee.Enabled || Comp.Active is not { Stage: 101 or 104 }) return;
        Comp.Active = null;
        Player.Status.CurrentStatus = "city";
        MarkDirty();
    }

    public void Reset()
    {
        Comp.Active = null;
        Comp.Settled.Clear();
        MarkDirty();
    }

    private static Dictionary<string, object> RewardReply(CombatInstance battle) => new()
    {
        ["t"] = battle.Type, ["s"] = battle.Stage, ["h"] = battle.Heroes.ToArray(), ["x"] = battle.ExtraHeroes.ToArray(),
        ["r"] = ReadJson(battle.RewardJson), ["w"] = battle.Win,
    };

    public static Dictionary<string, object> ReadJson(string json) => string.IsNullOrEmpty(json) ? [] : (Dictionary<string, object>)MainlineTable.Decode(JsonSerializer.Deserialize<JsonElement>(json))!;
}
