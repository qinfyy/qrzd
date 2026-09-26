using System.Globalization;
using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class CityLogic(Player player) : PlayerLogicBase(player)
{
    public const int DailyAction = 24;
    private CityComp Comp => Player.SaveData.CityComp;
    public int ActionVal { get => Comp.ActionVal; set { Comp.ActionVal = Math.Max(0, value); MarkDirty(); } }
    public int DevelopVal { get => Comp.DevelopVal; set { Comp.DevelopVal = value; MarkDirty(); } }
    public int DevelopValCount { get => Comp.DevelopValCount; set { Comp.DevelopValCount = value; MarkDirty(); } }
    public int BuildFund { get => Comp.BuildFund; set { Comp.BuildFund = value; MarkDirty(); } }
    public int ForceVal { get => Comp.FatigueVal; set { Comp.FatigueVal = value; MarkDirty(); } }
    public int FatigueVal { get => ForceVal; set => ForceVal = value; }
    public int EventVal { get => Comp.EventVal; set { Comp.EventVal = value; MarkDirty(); } }
    public int ResearchVal { get => Comp.ResearchVal; set { Comp.ResearchVal = value; MarkDirty(); } }
    public int PatrolNum { get => Comp.PatrolNum; set { Comp.PatrolNum = value; MarkDirty(); } }
    public int TotalActions => Comp.TotalActions;
    public int DailyConsumedAction => Comp.DailyConsumedAction;
    public int PatrolArea => Comp.PatrolArea;
    public int[] PatrolHeroes => Comp.PatrolHeroes.ToArray();
    public int CenterConfidence => Comp.CenterConfidence;
    public bool EndedActions => Comp.EndedActions;
    public bool NewbeePatrolComplete => Comp.NewbeePatrolComplete;
    public bool UseCanteen => Comp.UseCanteen;
    public IReadOnlyList<int> PassedStages => Comp.PassedStages;

    /// <summary>区域推进摘要，供 GM 诊断战斗入场与关卡进度使用。</summary>
    public IReadOnlyList<(int Id, string StatusName, int CurrentStage, int Passed, int Total)> StageSummary() =>
        Comp.Areas.OrderBy(area => area.Id).Select(area =>
        {
            string name = area.Status switch { 0 => "未解放", 1 => "可战斗", 2 => "已解放", _ => $"状态{area.Status}" };
            if (area.Lock) name += "/已锁";
            return (area.Id, name, area.CurrentStage, area.StageIds.Count(id => Comp.PassedStages.Contains(id)), area.StageIds.Count);
        }).ToArray();
    public AreaState? FindArea(int id) => Comp.Areas.FirstOrDefault(area => area.Id == id);
    public Dictionary<int, int> AreaLevels() => Comp.Areas.ToDictionary(area => area.Id, area => area.Level);
    public Dictionary<int, int[]> AreaBuildings() => Comp.Areas.ToDictionary(area => area.Id, area => area.Buildings.Select(value => value.BuildingId).ToArray());
    public Dictionary<int, int> AreaStates() => Comp.Areas.ToDictionary(area => area.Id, area => area.Cr && area.Status != 0 ? area.Status + 2 : area.Status);
    public Dictionary<int, int> Blackcores() => Comp.Blackcores.ToDictionary(pair => pair.Key, pair => pair.Value);
    public Dictionary<int, int> BlackcoreCounts() => new()
    {
        [0] = Comp.Blackcores.Values.Count(value => value == 0), [1] = Comp.Blackcores.Values.Count(value => value == 1),
        [2] = Comp.Blackcores.Values.Count(value => value == 2),
    };

    protected internal override void OnCreate() => Reset();

    public void EnsureDefaultAreas()
    {
        if (Comp.Areas.Count == 0) throw new InvalidOperationException("城市存档尚未初始化，请使用新账号");
    }

    public void Reset()
    {
        Comp.Areas.Clear();
        Comp.Blackcores.Clear();
        Comp.PassedStages.Clear();
        Comp.HiddenBuildings.Clear();
        Comp.PatrolHeroes.Clear();
        Comp.ActionVal = DailyAction;
        Comp.DailyConsumedAction = 0;
        Comp.TotalActions = Comp.TotalPatrols = Comp.TotalBuilds = Comp.TotalDevelops = 0;
        Comp.PatrolNum = 1;
        Comp.PatrolArea = 0;
        Comp.NewbeePatrolComplete = false;
        Comp.EndedActions = false;
        Comp.BuildFund = Comp.DevelopValCount = Comp.EventVal = Comp.CenterConfidence = 0;
        foreach (CityData row in GameTableCatalog.Instance.GetAllData<CityData>().Where(row => row.Id is >= 1 and <= 8 && !row.IsVirtualArea).OrderBy(row => row.Id))
        {
            AreaState area = new() { Id = row.Id, Status = row.Id == 1 ? 0 : 2, Level = 1, MaxBuildingNum = SlotCount(row.Id, 1) };
            area.StageIds.Add(PathForArea(row.Id));
            area.CurrentStage = area.StageIds.FirstOrDefault();
            if (row.Id == 1) area.Buildings.Add(new BuildingState { Slot = 1, BuildingId = 1, Uuid = Guid.NewGuid().ToString("N") });
            Comp.Areas.Add(area);
            Comp.Blackcores[row.Id] = 0;
        }
        RecalculateStats();
        MarkDirty();
    }

    private static int SlotCount(int area, int level) => GameTableCatalog.Instance.GetAllData<CityDevelopmentData>()
        .Count(row => row.CityId == area && row.LevelRequire <= level);

    private bool EligibleMission(MissionData mission)
    {
        if (!mission.Flag("release") || mission.Int("system_type") != 1 || mission.Int("startweeknum") > Player.WeekNum.Week) return false;
        if (mission.Int("cond_event") != 0 && !Player.EventTrigger.IsCompleted(mission.Int("cond_event"))) return false;
        return !mission.Flag("leaveCurArea");
    }

    private int[] PathForArea(int area)
    {
        HashSet<int> stages = GameTableCatalog.Instance.GetAllData<FightFirstWeekData>().Select(row => row.Id).ToHashSet();
        foreach (int eventId in Player.EventTrigger.Processing)
            stages.UnionWith(GameTableCatalog.Instance.GetDataById<EventContentData>(eventId)!.Ints("contentFinishBattle"));
        MissionData[] rows = GameTableCatalog.Instance.GetAllData<MissionData>().Where(row => row.Area == area && stages.Contains(row.Id) && EligibleMission(row)).ToArray();
        MissionData? current = rows.Where(row => row.Prerequisites.Length == 0).OrderByDescending(row => row.Int("cond_event") != 0).ThenBy(row => row.Id).FirstOrDefault();
        List<int> path = [];
        while (current is not null && !path.Contains(current.Id))
        {
            path.Add(current.Id);
            current = rows.Where(row => current.NextStages.Contains(row.Id) && row.Prerequisites.All(path.Contains))
                .OrderByDescending(row => row.Int("cond_event") != 0).ThenBy(row => row.Id).FirstOrDefault();
        }
        return path.ToArray();
    }

    public void RefreshStages()
    {
        foreach (AreaState area in Comp.Areas)
        {
            int[] path = PathForArea(area.Id);
            area.StageIds.Clear();
            area.StageIds.Add(path);
            if (area.Status != 0) area.CurrentStage = path.FirstOrDefault(id => !Comp.PassedStages.Contains(id));
        }
        MarkDirty();
    }

    public Dictionary<string, object> ToStagesSnapshot(int areaId)
    {
        AreaState? area = FindArea(areaId);
        if (area is null) return new() { ["cs"] = 0, ["sl"] = Array.Empty<object>() };
        return new() { ["cs"] = area.Status == 0 ? 0 : area.CurrentStage, ["sl"] = area.StageIds.Select(id => (object)new object[] { id }).ToArray() };
    }

    public Dictionary<string, object>? ToAreaSnapshot(int areaId)
    {
        AreaState? area = FindArea(areaId);
        if (area is null) return null;
        return new()
        {
            ["id"] = area.Id, ["st"] = new object[] { area.Status, area.Cr, area.Lock }, ["lv"] = area.Level, ["mbn"] = area.MaxBuildingNum,
            ["bd"] = area.Buildings.ToDictionary(building => building.Slot.ToString(), building => (object)new Dictionary<string, object>
            {
                ["id"] = building.BuildingId, ["uuid"] = building.Uuid,
            }),
            ["dbd"] = new Dictionary<string, object>(), ["rv"] = area.ResearchVal, ["fv"] = area.ForceVal, ["dv"] = area.DevelopVal,
            ["pl"] = area.PatrolLock, ["stage"] = ToStagesSnapshot(areaId),
        };
    }

    public Dictionary<string, object> ToAreasSnapshot() => Comp.Areas.ToDictionary(area => $"area_{area.Id}", area => (object)ToAreaSnapshot(area.Id)!);

    public Dictionary<string, object> ToCityDataSnapshot() => new()
    {
        ["dv"] = DevelopVal, ["dvc"] = DevelopValCount, ["av"] = ActionVal, ["pv"] = 240, ["lpc"] = Player.Profile.CreateTime,
        ["bf"] = BuildFund, ["fv"] = ForceVal, ["ev"] = EventVal, ["rv"] = ResearchVal, ["pn"] = PatrolNum, ["cc"] = CenterConfidence,
        ["tpn"] = Comp.TotalPatrols, ["tvn"] = Comp.TotalDevelops, ["tbn"] = Comp.TotalBuilds, ["nbb"] = !Player.Newbee.IsFinished(40),
        ["acr"] = Array.Empty<object>(), ["uc"] = Comp.UseCanteen, ["rbn"] = 0, ["hb"] = Comp.HiddenBuildings.ToArray(), ["areas"] = ToAreasSnapshot(),
    };

    public void Synchronize(bool settlement = false) => Notify("get_all_area_info_reply", new()
    {
        ["ais"] = ToAreasSnapshot(), ["cd"] = ToCityDataSnapshot(), ["sm"] = settlement,
    });

    public void RecalculateStats()
    {
        Comp.FatigueVal = Comp.ResearchVal = Comp.DevelopVal = 0;
        foreach (AreaState area in Comp.Areas)
        {
            area.ResearchVal = area.ForceVal = area.DevelopVal = 0;
            foreach (BuildingState building in area.Buildings)
            {
                if (!GameTableCatalog.Instance.TryGetDataById<CityBuildingData>(building.BuildingId, out var row)) continue;
                area.ResearchVal += row.ResearchVal;
                area.ForceVal += row.ForceVal;
                area.DevelopVal += row.DevVal;
            }
            if (area.Status != 0) continue;
            Comp.ResearchVal += area.ResearchVal;
            Comp.FatigueVal += area.ForceVal;
            Comp.DevelopVal += area.DevelopVal;
        }
        MarkDirty();
    }

    public bool CanAct(int cost) => ActionBlocker(cost) is null;

    /// <summary>
    /// 返回阻止行动的原因，全部满足时为 null。原先 CanAct 只给 bool，调用方无法区分
    /// “行动力不足”和“已结束行动/紧急状态/战斗中等”，日志里只能看到一个笼统结论。
    /// </summary>
    public string? ActionBlocker(int cost)
    {
        if (cost < 0) return "行动消耗为负数";
        if (ActionVal < cost) return $"行动力不足，剩余 {ActionVal}，需要 {cost}";
        if (Player.WeekNum.Week != 0) return $"非首周（第 {Player.WeekNum.Week} 周目）无法行动";
        if (Player.WeekNum.Day >= 7) return "本周目已进入第 7 天";
        if (Player.WeekNum.EndingId != 0) return "结局播放中无法行动";
        if (Player.Combat.IsActive) return "战斗进行中";
        if (Player.Status.Emergency) return "紧急状态无法行动";
        if (Comp.EndedActions) return "今日行动已结束（需跨日或重新登录才恢复）";
        return null;
    }

    private bool ValidateAreaTeam(int areaId, int cost, int fatigue, int[] heroes, out string? error)
    {
        error = null;
        if (!CanAct(cost)) error = "当前状态不可行动或行动力不足";
        else if (FindArea(areaId) is not { Status: 0, Lock: false }) error = "区域未解放或已锁定";
        else if (!Player.HeroMgr.ValidateTeam(heroes, fatigue)) error = "队伍包含重复、未拥有或疲劳不足的神器使";
        else if (!CanGrantActionRewards(cost)) error = "行动奖励配置缺失";
        return error is null;
    }

    public bool CanGrantActionRewards(int cost)
    {
        for (int i = TotalActions + 1; i <= TotalActions + cost / 2; i++)
        {
            if (GameTableCatalog.Instance.GetDataById<CityFirstWeekData>(i) is { } row && !RewardLogic.CanGrant(Player, row.Ints("reward"))) return false;
        }
        return true;
    }

    public void ConsumeAction(int cost)
    {
        if (cost < 0 || ActionVal < cost) throw new InvalidOperationException("行动力不足");
        Comp.ActionVal -= cost;
        Comp.DailyConsumedAction += cost;
        Comp.TotalActions += cost / 2;
        FinishPatrol();
        MarkDirty();
    }

    public bool Build(int areaId, int slot, int buildingId, int[] heroes, out string? error)
    {
        error = null;
        CityBuildingData? data = GameTableCatalog.Instance.GetDataById<CityBuildingData>(buildingId);
        if (data is null || data.Ban != 0 || data.ClientBan != 0) { error = "该建筑不可建造"; return false; }
        if (GameTableCatalog.Instance.GetDataById<CityWeekBuildingData>(Player.WeekNum.Week + 1) is { } allowed && !allowed.Ints("buildings").Contains(buildingId))
        { error = "本周目尚未解锁该建筑"; return false; }
        if (!ValidateAreaTeam(areaId, data.Ac, data.Fc, heroes, out error)) return false;
        AreaState area = FindArea(areaId)!;
        if (slot < 1 || slot > area.MaxBuildingNum || area.Buildings.Any(building => building.Slot == slot)) error = "建筑槽位无效或已占用";
        else if (Player.Profile.Money < data.MoneyCost || ResearchVal < data.ResearchRequire) error = "金币或科技不足";
        else if (heroes.Sum(Player.HeroMgr.GetConstructValue) < data.Cr) error = "建设力不足";
        else if (data.MaxLimit > 0 && Comp.Areas.Sum(value => value.Buildings.Count(building => building.BuildingId == buildingId)) >= data.MaxLimit)
            error = "建筑已达到全城数量上限";
        else if (data.AreaLimit.Count > 0 && (!data.AreaLimit.TryGetValue(areaId, out int limit) ||
            area.Buildings.Count(building => building.BuildingId == buildingId) >= limit)) error = "不符合建筑区域要求";
        else if (data.Pre.Any(id => !Comp.Areas.Any(value => value.Buildings.Any(building => building.BuildingId == id)))) error = "缺少前置建筑";
        else if (data.AreaBuildingRequire.Any(pair => area.Buildings.Count(building => building.BuildingId == pair.Key) < pair.Value)) error = "缺少本区域前置建筑";
        else if (data.Init == 0 && !Comp.HiddenBuildings.Contains(buildingId)) error = "建筑尚未解锁";
        if (error is not null) return false;
        Player.HeroMgr.ConsumeTeamFatigue(heroes, data.Fc);
        Player.Profile.AddMoney(-data.MoneyCost);
        ConsumeAction(data.Ac);
        area.Buildings.Add(new BuildingState { Slot = slot, BuildingId = buildingId, Uuid = Guid.NewGuid().ToString("N") });
        Comp.TotalBuilds++;
        RecalculateStats();
        var reward = RewardLogic.GrantAction(Player, GameTableCatalog.Instance.GetDataById<BuildRewardData>(Player.Profile.Level)!, data.Ac);
        Synchronize();
        Notify("area_build_reply", new() { ["rs"] = true, ["aid"] = areaId, ["ext"] = new Dictionary<string, object> { ["reward"] = reward } });
        return true;
    }

    public bool LevelUpArea(int areaId, int levelUpVal, int[] heroes, out string? error)
    {
        error = null;
        AreaState? area = FindArea(areaId);
        CityUpgradeData? data = area is null ? null : GameTableCatalog.Instance.GetDataById<CityUpgradeData>(CityUpgradeData.MakeId(areaId, area.Level + 1));
        if (levelUpVal != 1 || area is null || data is null) { error = "开发等级无效或已达上限"; return false; }
        if (!ValidateAreaTeam(areaId, data.ActionConsume, data.Fatigue, heroes, out error)) return false;
        if (heroes.Sum(Player.HeroMgr.GetLeadershipValue) < data.LeadRequire) error = "开发力不足";
        else if (Player.Profile.Money < data.MoneyConsume) error = "金币不足";
        if (error is not null) return false;
        Player.HeroMgr.ConsumeTeamFatigue(heroes, data.Fatigue);
        Player.Profile.AddMoney(-data.MoneyConsume);
        ConsumeAction(data.ActionConsume);
        area.Level++;
        area.MaxBuildingNum = SlotCount(areaId, area.Level);
        Comp.TotalDevelops++;
        var reward = RewardLogic.GrantAction(Player, GameTableCatalog.Instance.GetDataById<DevelopRewardData>(Player.Profile.Level)!, data.ActionConsume);
        Synchronize();
        Notify("area_level_up_reply", new() { ["rs"] = true, ["aid"] = areaId, ["ext"] = new Dictionary<string, object> { ["reward"] = reward } });
        MarkDirty();
        return true;
    }

    public bool Patrol(int areaId, int[] heroes, out string? error)
    {
        error = null;
        AreaState? area = FindArea(areaId);
        CityPatrolData? data = area is null ? null : GameTableCatalog.Instance.GetDataById<CityPatrolData>(CityPatrolData.MakeId(areaId, area.Level));
        if (area is null || data is null || area.PatrolLock) { error = "区域不可巡查"; return false; }
        if (!ValidateAreaTeam(areaId, data.ActionConsume, data.Fatigue, heroes, out error)) return false;
        if (heroes.Sum(Player.HeroMgr.GetInsightValue) < data.InsightRequirement(PatrolNum) + Player.Intelligence.PatrolPenalty(areaId)) { error = "巡查力不足"; return false; }
        Player.HeroMgr.ConsumeTeamFatigue(heroes, data.Fatigue);
        ConsumeAction(data.ActionConsume);
        Comp.PatrolArea = areaId;
        Comp.PatrolHeroes.Add(heroes);
        Comp.PatrolNum++;
        Comp.TotalPatrols++;
        area.PatrolCount++;
        foreach (int hero in heroes) Player.HeroMgr.AddFriendly(hero, data.FavorReward);
        Player.HeroMgr.Synchronize();
        Player.EventTrigger.SetOperation(2, true);
        var reward = RewardLogic.GrantAction(Player, GameTableCatalog.Instance.GetDataById<PatrolRewardData>(Player.Profile.Level)!, data.ActionConsume);
        Synchronize();
        Notify("notify_add_friendly", new() { ["fr"] = heroes.ToDictionary(id => id, _ => data.FavorReward), ["n"] = new Dictionary<string, object>() });
        Notify("enter_patrol_reply", new()
        {
            ["s"] = true, ["rs"] = true, ["ext"] = new Dictionary<string, object> { ["reward"] = reward },
            ["ev"] = Player.EventTrigger.Available().Where(row => row.Patrol).Select(row => row.Id).ToArray(),
        });
        MarkDirty();
        return true;
    }

    public void FinishPatrol()
    {
        Comp.PatrolArea = 0;
        Comp.PatrolHeroes.Clear();
        Player.EventTrigger.SetOperation(2, false);
        MarkDirty();
    }

    public bool DestroyBuilding(string uuid, out int affectedAreaId)
    {
        affectedAreaId = 0;
        if (!CanAct(0)) return false;
        foreach (AreaState area in Comp.Areas.Where(area => area.Status == 0 && !area.Lock))
        {
            BuildingState? building = area.Buildings.FirstOrDefault(value => value.Uuid == uuid);
            if (building is null || GameTableCatalog.Instance.GetDataById<CityBuildingData>(building.BuildingId)?.CanRemove != 1) continue;
            area.Buildings.Remove(building);
            affectedAreaId = area.Id;
            RecalculateStats();
            return true;
        }
        return false;
    }

    public Dictionary<string, object>? Rest()
    {
        int cost = ActionVal;
        if (cost <= 0 || !CanAct(cost) || Player.EventTrigger.HasBlockingEvents() || !CanGrantActionRewards(cost)) return null;
        string key = $"ac{cost}";
        int level = Player.Profile.Level;
        int money = GameTableCatalog.Instance.GetDataById<RestMoneyData>(level)!.Int(key);
        int exp = GameTableCatalog.Instance.GetDataById<RestExperienceData>(level)!.Int(key);
        int fatigue = GameTableCatalog.Instance.GetDataById<RestFatigueData>(level)!.Int(key);
        int before = TotalActions;
        ConsumeAction(cost);
        Dictionary<string, object> reward = Player.Profile.AddExperience(exp);
        Player.Profile.AddMoney(money);
        Player.HeroMgr.RestoreAllFatigue(fatigue);
        reward["money"] = money;
        reward["fatigue"] = fatigue;
        for (int i = before + 1; i <= TotalActions; i++)
        {
            if (GameTableCatalog.Instance.GetDataById<CityFirstWeekData>(i) is { } row) RewardLogic.Merge(reward, RewardLogic.Grant(Player, row.Ints("reward")));
        }
        return reward;
    }

    public bool Zhai() => Rest() is not null;

    /// <summary>
    /// 深夜食堂。客户端 canteenRequest 传入选中的神器使，最多 3 名，
    /// 每人按 fatigue_recover 的 ext_fatigue_recover 回复疲劳，每天只能用餐一次。
    /// 客户端没有专门的 reply 方法，靠回调拿结果、靠 syncHeroesData 刷新疲劳动画，
    /// 因此这里必须推一次 HeroMgr.Synchronize。
    /// </summary>
    public Dictionary<string, object> Canteen(int[] heroes, out string? error)
    {
        error = null;
        if (Comp.UseCanteen)
            error = "深夜食堂今天已经用过了";
        else if (Player.WeekNum.Week != 0 || Player.WeekNum.Day >= 7 || Player.WeekNum.EndingId != 0)
            error = "当前不在首周，深夜食堂不可用";
        else if (Player.Status.Emergency)
            error = "紧急状态下无法用餐";
        else if (heroes.Length is < 1 or > 3)
            error = heroes.Length == 0 ? "请先选择神器使" : "最多选择 3 名神器使";
        else if (heroes.Distinct().Count() != heroes.Length)
            error = "不能重复选择同一名神器使";
        else if (heroes.Any(id => Player.HeroMgr.Find(id) is null || !Player.HeroMgr.AvailableHeroIds.Contains(id)))
            error = "队伍包含未拥有或当前不可用的神器使";
        if (error is not null)
            return new Dictionary<string, object>();

        int amount = GameTableCatalog.Instance.GetDataById<FatigueRecoveryData>(Player.WeekNum.Week)?.Int("ext_fatigue_recover") ?? 25;
        Dictionary<int, int> before = heroes.ToDictionary(id => id, id => Player.HeroMgr.Find(id)!.Fatigue);
        foreach (int id in heroes) Player.HeroMgr.AddFatigue(id, amount);
        Comp.UseCanteen = true;
        MarkDirty();
        Player.HeroMgr.Synchronize();
        Dictionary<string, object> result = new();
        foreach (int id in heroes)
            result[id.ToString(CultureInfo.InvariantCulture)] = Player.HeroMgr.Find(id)!.Fatigue - before[id];
        return result;
    }

    public void PassAreaStage(int areaId, int stageId)
    {
        AreaState? area = FindArea(areaId);
        if (area is null || area.Status != 1 || area.CurrentStage != stageId || Comp.PassedStages.Contains(stageId)) return;
        Comp.PassedStages.Add(stageId);
        area.CurrentStage = area.StageIds.FirstOrDefault(id => !Comp.PassedStages.Contains(id));
        if (area.CurrentStage == 0)
        {
            area.Status = 0;
            UnlockAdjacentAreas();
        }
        RecalculateStats();
    }

    private void UnlockAdjacentAreas()
    {
        foreach (AreaState other in Comp.Areas.Where(value => value.Status == 2))
        {
            CityData row = GameTableCatalog.Instance.GetDataById<CityData>(other.Id)!;
            List<int> requirements = row.AreaRequire.GetValueOrDefault(Player.Story.Route) ?? row.AreaRequire.GetValueOrDefault("-1") ?? [];
            if (requirements.Count > 0 && requirements.All(id => id == 0 || FindArea(id)?.Status == 0)) other.Status = 1;
        }
    }

    internal void SkipIntroductionBattle()
    {
        if (Player.Newbee.Enabled) throw new InvalidOperationException("新手引导已启用");
        AreaState area = FindArea(1) ?? throw new InvalidOperationException("缺少中央庭存档");
        area.Status = 0;
        area.CurrentStage = 0;
        UnlockAdjacentAreas();
        RefreshStages();
        RecalculateStats();
        MarkDirty();
    }

    public void ApplyEvent(EventContentData row)
    {
        foreach ((string field, int status) in new[] { ("areaFree", 0), ("areaFight", 1), ("areaFall", 2) })
        {
            foreach (int id in row.Ints(field))
            {
                if (FindArea(id) is { } area) area.Status = status;
            }
        }
        foreach (int id in row.Ints("areaLock"))
        {
            if (FindArea(id) is { } area)
                area.Lock = true;
        }
        foreach (int id in row.Ints("areaUnlock")) { if (FindArea(id) is { } area) area.Lock = false; }
        foreach (int id in row.Ints("areaCR7")) { if (FindArea(id) is { } area) area.Cr = true; }
        foreach (var pair in row.Map("changeBlackcore")) SetBlackcore(pair.Key, MainlineTable.Number(pair.Value));
        foreach (var pair in row.Map("batchChangeBlackcore"))
        {
            foreach (int id in Comp.Blackcores.Where(value => value.Value == pair.Key).Select(value => value.Key).ToArray())
                SetBlackcore(id, MainlineTable.Number(pair.Value));
        }
        foreach (int building in row.Ints("hiddenBuilding")) { if (!Comp.HiddenBuildings.Contains(building)) Comp.HiddenBuildings.Add(building); }
        if (row.Flag("complete_newbee_patrol")) Comp.NewbeePatrolComplete = true;
        foreach (var pair in row.Map("buildBuilding"))
        {
            var values = MainlineTable.Elements(pair.Value);
            AreaState area = FindArea(pair.Key)!;
            int id = MainlineTable.Number(values[0]);
            int count = MainlineTable.Number(values[1]);
            for (int i = 0; i < count; i++)
            {
                int slot = Enumerable.Range(1, area.MaxBuildingNum).First(value => area.Buildings.All(building => building.Slot != value));
                area.Buildings.Add(new BuildingState { Slot = slot, BuildingId = id, Uuid = Guid.NewGuid().ToString("N") });
            }
        }
        foreach (int id in row.Ints("destroyPatrolBuild"))
            if (FindArea(PatrolArea) is { } area) foreach (var building in area.Buildings.Where(value => value.BuildingId == id).ToArray()) area.Buildings.Remove(building);
        foreach (var tuple in row.List("destroyAreaPatrolBuild"))
        {
            var pair = MainlineTable.Elements(tuple);
            if (FindArea(MainlineTable.Number(pair[0])) is { } area)
                foreach (var building in area.Buildings.Where(value => value.BuildingId == MainlineTable.Number(pair[1])).ToArray()) area.Buildings.Remove(building);
        }
        RefreshStages();
        RecalculateStats();
    }

    public bool CanApplyEvent(EventContentData row)
    {
        if (row.List("destroyAreaPatrolBuild").Any(value => MainlineTable.Elements(value).Length != 2)) return false;
        foreach (string field in new[] { "areaFree", "areaFight", "areaFall", "areaLock", "areaUnlock", "areaCR7" })
            if (row.Ints(field).Any(id => FindArea(id) is null))
                return false;

        foreach (var pair in row.Map("changeBlackcore"))
            if (FindArea(pair.Key) is null || MainlineTable.Number(pair.Value, -1) is < 0 or > 2)
                return false;

        foreach (var pair in row.Map("batchChangeBlackcore"))
            if (pair.Key is < 0 or > 2 || MainlineTable.Number(pair.Value, -1) is < 0 or > 2)
                return false;

        foreach (var pair in row.Map("buildBuilding"))
        {
            var values = MainlineTable.Elements(pair.Value);
            if (values.Length != 2 || FindArea(pair.Key) is not { } area) return false;
            if (GameTableCatalog.Instance.GetDataById<CityBuildingData>(MainlineTable.Number(values[0])) is null) return false;
            int count = MainlineTable.Number(values[1]);
            if (count < 0 || area.Buildings.Count + count > area.MaxBuildingNum) return false;
        }
        return row.Ints("intelligence").All(Player.Intelligence.CanAdd);
    }

    public void SetBlackcore(int area, int state)
    {
        if (!Comp.Blackcores.ContainsKey(area) || state is < 0 or > 2) throw new InvalidOperationException("黑核状态无效");
        Comp.Blackcores[area] = state;
        Notify("updateBlackCore", new() { ["c"] = state, ["b"] = area });
        MarkDirty();
    }

    public void ApplyReward(RewardData row)
    {
        if (row.Has("buildFund")) BuildFund = checked(BuildFund + row.Int("buildFund"));
        foreach (int id in row.Ints("buildableBuilding")) if (!Comp.HiddenBuildings.Contains(id)) Comp.HiddenBuildings.Add(id);
        foreach (var tuple in row.List("destroyBuliding"))
        {
            var values = MainlineTable.Elements(tuple);
            AreaState? area = FindArea(MainlineTable.Number(values[0]));
            if (area is null) continue;
            foreach (BuildingState building in area.Buildings.Where(value => value.BuildingId == MainlineTable.Number(values[1])).ToArray()) area.Buildings.Remove(building);
        }
        if (row.Has("buildableBuilding") || row.Has("destroyBuliding")) RecalculateStats();
        MarkDirty();
    }

    public bool ConsumeIntelligence(int amount)
    {
        if (amount < 0 || DevelopValCount < amount) return false;
        DevelopValCount -= amount;
        return true;
    }

    public void RefreshIntelligence() => DevelopValCount = DevelopVal;

    public void DestroyIntelligenceBuilding(out int areaId, out int buildingId)
    {
        areaId = buildingId = 0;
        var candidates = Comp.Areas.Where(area => area.Status == 0).SelectMany(area => area.Buildings.Select(building => (area, building)))
            .Where(pair => GameTableCatalog.Instance.GetDataById<CityBuildingData>(pair.building.BuildingId) is { Type: 1 or 2 or 3, CanRemove: 1 }).ToArray();
        if (candidates.Length == 0) return;
        var selected = candidates[Random.Shared.Next(candidates.Length)];
        areaId = selected.area.Id;
        buildingId = selected.building.BuildingId;
        selected.area.Buildings.Remove(selected.building);
        RecalculateStats();
    }

    public void EndActions() { Comp.EndedActions = true; MarkDirty(); }

    public bool PrepareNextDay()
    {
        if (Player.WeekNum.CanAdvance() != "ok") return false;
        if (ActionVal > 0) ConsumeAction(ActionVal);
        EndActions();
        Synchronize();
        return true;
    }

    public void NewDay(bool final)
    {
        Comp.ActionVal = final ? 0 : DailyAction;
        Comp.DailyConsumedAction = 0;
        Comp.PatrolNum = 1;
        Comp.EndedActions = false;
        // 深夜食堂每日一次，跨日恢复资格。
        Comp.UseCanteen = false;
        FinishPatrol();
        MarkDirty();
    }
}
