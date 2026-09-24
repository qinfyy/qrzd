using Serilog;
using Sv.Configuration;
using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class NewbeeLogic(Player player) : PlayerLogicBase(player)
{
    private static readonly ILogger Logger = Log.ForContext<NewbeeLogic>();
    private NewbeeComp Comp => Player.SaveData.NewbeeComp;
    public bool Enabled => Config.Server.EnableNewbieTutorial;
    public bool IsFinished(int id) => !Enabled || Comp.Finished.Contains(id);
    public bool CanSummon => Player.WeekNum.Week == 0 && Player.WeekNum.Day == 0 && Player.EventTrigger.IsCompleted(1021);
    public bool SummonClaimed => Comp.SummonClaimed;
    public int[] Processing => Enabled ? Comp.Processing.ToArray() : [];

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["fn"] = Enabled ? Comp.Finished.ToArray() : GameTableCatalog.Instance.GetAllData<NewbeeData>().Select(row => row.Id).Union(Comp.Finished).ToArray(),
        ["pe"] = Processing, ["ip"] = Enabled && Comp.InitPrologue, ["id"] = Enabled,
    };
    public Dictionary<string, object> ToSummonSnapshot() => new() { ["ct"] = SummonClaimed ? 1 : 0, ["iof"] = Comp.SummonOpen };

    protected internal override void OnLogin() => ApplyConfiguration();

    internal void ApplyConfiguration()
    {
        if (Enabled) return;
        if (!Comp.IntroductionSkipped && Player.WeekNum.Week == 0 && Player.WeekNum.Day == 0 && Player.Story.Route == "1" &&
            Player.WeekNum.EndingId == 0 && !Player.EventTrigger.IsCompleted(1047))
        {
            // Skip only the introduction. Event rewards remain one-shot, and no battle result is forged.
            foreach (int id in new[] { 1, 3, 6, 1000, 1006 }) Player.EventTrigger.SkipTutorialEvent(id);
            foreach (int id in new[] { 1016, 1017, 1041, 1020 })
                if (Player.EventTrigger.Find(id) is not null) Player.EventTrigger.SkipTutorialEvent(id);
            if (!new[] { 1015, 1016, 1017 }.Any(Player.EventTrigger.IsCompleted)) Player.EventTrigger.SkipTutorialEvent(1015);
            foreach (int id in new[] { 1018, 1019, 8, 1001, 1002, 1004, 1021 }) Player.EventTrigger.SkipTutorialEvent(id);

            if (!SummonClaimed && (!OpenSummon(0) || DrawSummon(0, 2) is null)) throw new InvalidOperationException("跳过引导时教学召唤配置无效");
            CloseSummon(0);
            int[] introductions = [1038, 1039, 1040];
            if (!introductions.Any(Player.EventTrigger.IsCompleted))
            {
                int introduction = introductions.First(id => GameTableCatalog.Instance.GetDataById<EventConditionData>(id)!.Rules[24].Integers.Contains(Comp.SummonHero));
                Player.EventTrigger.SkipTutorialEvent(introduction);
            }
            Player.Combat.SkipIntroduction();
            Player.EventTrigger.SkipTutorialEvent(1043);
            Player.City.SkipIntroductionBattle();
            foreach (int id in new[] { 10000, 1007, 1008, 1009, 1022, 1010, 1011, 1025, 1013, 1005, 1024, 1047 }) Player.EventTrigger.SkipTutorialEvent(id);
            if (Player.City.PatrolArea == 1) Player.City.FinishPatrol();
            foreach (int id in new[] { 10, 11, 30, 40, 41, 42, 43, 44, 90, 93 }.SelectMany(Chain).Distinct())
                if (!Comp.Finished.Contains(id)) Comp.Finished.Add(id);
            Comp.InitPrologue = false;
            Comp.TutorialBegan = true;
            Comp.IntroductionSkipped = true;
            Player.Status.SetEmergency(false);
            Player.Status.CurrentStatus = "city";
            Player.Story.SetPlace(0);
            Logger.Information("City UID={Uid} 已按配置跳过新手引导，保留 week={Week} day={Day} action={Action}",
                Player.Uid, Player.WeekNum.Week, Player.WeekNum.Day, Player.City.ActionVal);
            MarkDirty();
        }
        if (Comp.Processing.Count > 0)
        {
            Comp.Processing.Clear();
            MarkDirty();
        }
        Player.EventTrigger.Refresh(false);
    }

    public bool CanTrigger(int id)
    {
        if (!Enabled) return false;
        NewbeeData? row = GameTableCatalog.Instance.GetDataById<NewbeeData>(id);
        if (row is null || row.Flag("battle_newbee") || !Player.Story.ClientEventFinished) return false;
        if (IsFinished(id) && !row.Flag("multi_trigger")) return false;
        if (!row.Ints("require_newbee").All(IsFinished)) return false;
        if (!row.Ints("require_event_and").All(Player.EventTrigger.IsCompleted)) return false;
        if (row.Ints("require_not_event_and").Any(value => Player.EventTrigger.IsCompleted(value) && Player.EventTrigger.Find(value) is null)) return false;
        int[] any = row.Ints("require_event_or");
        int[] absent = row.Ints("require_not_event_or");
        if (any.Length + absent.Length > 0 && !any.Any(Player.EventTrigger.IsCompleted) && !absent.Any(value => !Player.EventTrigger.IsCompleted(value))) return false;
        int[] target = row.Ints("require_target_event");
        if (target.Length > 0 && !target.Any(value => Player.EventTrigger.Find(value) is not null)) return false;
        foreach (var area in row.List("require_area"))
        {
            var values = MainlineTable.Elements(area);
            if (values.Length != 2 || Player.City.FindArea(MainlineTable.Number(values[0]))?.Status != MainlineTable.Number(values[1])) return false;
        }
        int[] special = row.Ints("require_special");
        if (special.Length > 0)
        {
            if (special[0] == 1 && !Player.Inventory.Counts().Keys.Any(value => GameTableCatalog.Instance.GetDataById<ItemData>(value)?.Type == 5)) return false;
            if (special[0] is 2 or 3 && (special.Length < 2 || Player.WeekNum.Week < special[1] - 1)) return false;
            if (special[0] == 3 && (special.Length < 3 || (Player.WeekNum.Week == special[1] - 1 && Player.WeekNum.Day < special[2] - 1))) return false;
            if (special[0] is < 1 or > 4) return false;
        }
        return true;
    }

    public bool Request(int id)
    {
        if (!Enabled)
        {
            Synchronize();
            return GameTableCatalog.Instance.GetDataById<NewbeeData>(id) is not null;
        }
        if (!CanTrigger(id)) return false;
        NewbeeData row = GameTableCatalog.Instance.GetDataById<NewbeeData>(id)!;
        if (!row.Flag("begin_newbee") && !Comp.Processing.Any(root => Chain(root).Contains(id))) return false;
        if (!Comp.Processing.Contains(id)) { Comp.Processing.Add(id); MarkDirty(); }
        Notify("triggerClientNewbee", new() { ["e"] = id });
        return true;
    }

    private static IEnumerable<int> Chain(int id)
    {
        HashSet<int> visited = [];
        while (id != 0 && visited.Add(id))
        {
            yield return id;
            id = GameTableCatalog.Instance.GetDataById<NewbeeData>(id)?.Next ?? 0;
        }
    }

    public bool Finish(int[] ids)
    {
        if (ids.Length == 0 || ids.Length > 20 || ids.Distinct().Count() != ids.Length) return false;
        if (ids.Any(id => GameTableCatalog.Instance.GetDataById<NewbeeData>(id) is null)) return false;
        if (!Enabled) { Synchronize(); return true; }
        HashSet<int> allowed = Comp.Processing.ToHashSet();
        if (Player.Combat.Active is { State: 1, Type: 2, Stage: 104, EventId: 1 }) allowed.UnionWith([40, 41, 42, 43, 44]);
        foreach (int root in Comp.Processing)
        {
            foreach (int step in Chain(root)) allowed.UnionWith(GameTableCatalog.Instance.GetDataById<NewbeeData>(step)!.Ints("complete_newbee"));
        }
        if (ids.Any(id => !allowed.Contains(id) && !IsFinished(id))) return false;
        foreach (int id in ids)
        {
            if (!IsFinished(id)) Comp.Finished.Add(id);
            Comp.Processing.Remove(id);
        }
        MarkDirty();
        Synchronize();
        return true;
    }

    public void Synchronize() => Notify("syncTriggerEventList", new()
    {
        ["el"] = GameTableCatalog.Instance.GetAllData<NewbeeData>().Where(row => row.Flag("begin_newbee") && CanTrigger(row.Id)).Select(row => row.Id).ToArray(),
    });

    public void BeginTutorial() { Comp.TutorialBegan = true; MarkDirty(); }

    public bool PassPrologue()
    {
        if (!Enabled || !Comp.InitPrologue) return true;
        if (Player.Combat.Active is not { State: 1, Type: 2, Stage: 104, EventId: 1 } && !Player.EventTrigger.IsCompleted(6)) return false;
        Comp.InitPrologue = false;
        MarkDirty();
        return true;
    }

    public bool OpenSummon(int type)
    {
        if (type != 0 || !CanSummon || SummonClaimed) return false;
        Comp.SummonOpen = true;
        MarkDirty();
        return true;
    }

    public Dictionary<string, object>? DrawSummon(int type, int index)
    {
        if (type != 0 || !CanSummon || !Comp.SummonOpen || index != 2) return null;
        if (!Comp.SummonClaimed)
        {
            // The three tutorial introductions identify the original tutorial pool.
            int[] pool = new[] { 1038, 1039, 1040 }.Select(id => GameTableCatalog.Instance.GetDataById<EventConditionData>(id))
                .OfType<EventConditionData>().SelectMany(row => row.Rules.TryGetValue(24, out var rule) ? rule.Integers : Array.Empty<int>()).Distinct().ToArray();
            if (pool.Length == 0) return null;
            Comp.SummonHero = pool[Random.Shared.Next(pool.Length)];
            Player.HeroMgr.Unlock(Comp.SummonHero);
            Comp.SummonClaimed = true;
            MarkDirty();
        }
        return new() { ["index"] = index, ["hero"] = new[] { Comp.SummonHero }, ["get_hero"] = true, ["type"] = type };
    }

    public Dictionary<string, object> SummonFile() => SummonClaimed ? new() { ["2"] = new Dictionary<string, object>
    {
        ["index"] = 2, ["hero"] = new[] { Comp.SummonHero }, ["get_hero"] = true,
    } } : [];

    public bool CloseSummon(int type)
    {
        if (type != 0 || !SummonClaimed) return false;
        Comp.SummonOpen = false;
        MarkDirty();
        return true;
    }

    public void Reset()
    {
        Comp.Finished.Clear();
        Comp.Processing.Clear();
        Comp.InitPrologue = true;
        Comp.TutorialBegan = Comp.SummonClaimed = Comp.SummonOpen = false;
        Comp.IntroductionSkipped = false;
        Comp.SummonHero = 0;
        MarkDirty();
    }
}
