using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class NewbeeLogic(Player player) : PlayerLogicBase(player)
{
    private NewbeeComp Comp => Player.SaveData.NewbeeComp;
    public bool IsFinished(int id) => Comp.Finished.Contains(id);
    public bool CanSummon => Player.WeekNum.Week == 0 && Player.WeekNum.Day == 0 && Player.EventTrigger.IsCompleted(1021);
    public bool SummonClaimed => Comp.SummonClaimed;
    public int[] Processing => Comp.Processing.ToArray();

    public Dictionary<string, object> ToSnapshot() => new() { ["fn"] = Comp.Finished.ToArray(), ["pe"] = Processing, ["ip"] = Comp.InitPrologue };
    public Dictionary<string, object> ToSummonSnapshot() => new() { ["ct"] = SummonClaimed ? 1 : 0, ["iof"] = Comp.SummonOpen };

    public bool CanTrigger(int id)
    {
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
        HashSet<int> allowed = Comp.Processing.ToHashSet();
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
        if (!Player.EventTrigger.IsCompleted(6)) return false;
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
        Comp.SummonHero = 0;
        MarkDirty();
    }
}
