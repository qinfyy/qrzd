using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;
using System.Text.Json;

namespace Sv.Game;

public sealed class StoryLogic(Player player) : PlayerLogicBase(player)
{
    private StoryComp Comp => Player.SaveData.StoryComp;
    public string Route => Comp.Route;
    public IReadOnlyList<int> Cgs => Comp.Cgs;
    public int[] CurrentCgs => Comp.CurrentCgs.ToArray();
    public IReadOnlyList<int> Endings => Comp.Endings;
    public int GeneralValue => Comp.GeneralValue;
    public bool ClientEventFinished => Comp.ClientEventFinish;

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["rt"] = Route, ["ed"] = Comp.Endings.ToArray(), ["cf"] = Comp.ClickedRoutes.ToArray(), ["cef"] = Comp.ClientEventFinish,
        ["lp"] = Comp.Place, ["ie"] = Comp.Endings.Count != 0, ["srt"] = Array.Empty<object>(), ["gv"] = Comp.GeneralValue,
        ["ae"] = AreaExclam(),
    };

    public bool Enter(string route)
    {
        if (route != "1" || Player.WeekNum.Week != 0 || Player.WeekNum.EndingId != 0) return false;
        if (Comp.Route != route) { Comp.Route = route; MarkDirty(); }
        return true;
    }

    public bool SetPlace(int place)
    {
        if (place is < 0 or > 2) return false;
        Comp.Place = place;
        MarkDirty();
        return true;
    }

    public void ClickRoute(string route)
    {
        if (route == "1" && !Comp.ClickedRoutes.Contains(route)) { Comp.ClickedRoutes.Add(route); MarkDirty(); }
    }

    public bool CanAddCg(int cg)
    {
        CgData? row = GameTableCatalog.Instance.GetDataById<CgData>(cg);
        if (row is null) return false;
        if (Comp.Cgs.Contains(cg)) return true;
        int[] rewards = row.Ints("rewards");
        // CG bonuses are terminal asset rewards, never another CG award chain.
        return rewards.All(id => GameTableCatalog.Instance.GetDataById<RewardData>(id)?.Ints("cg").Length == 0) && RewardLogic.CanGrant(Player, rewards);
    }

    public bool AddCg(int cg)
    {
        if (!CanAddCg(cg)) throw new InvalidOperationException($"CG 奖励配置暂不支持: {cg}");
        if (!Comp.CurrentCgs.Contains(cg)) { Comp.CurrentCgs.Add(cg); MarkDirty(); }
        if (Comp.Cgs.Contains(cg)) return false;
        Comp.Cgs.Add(cg);
        Notify("addCg", new() { ["c"] = new[] { cg } });
        var reward = RewardLogic.Grant(Player, GameTableCatalog.Instance.GetDataById<CgData>(cg)!.Ints("rewards"));
        if (reward.Count > 0) Notify("cgRewards", new() { ["r"] = reward });
        MarkDirty();
        return true;
    }

    public void AddEnding(int ending)
    {
        if (!Comp.Endings.Contains(ending)) Comp.Endings.Add(ending);
        MarkDirty();
    }

    public bool CanApplyEvent(EventContentData row)
    {
        if (!row.Has("exclam")) return true;
        if (row.Get("exclam").ValueKind != JsonValueKind.Object) return false;
        foreach (var marker in row.Get("exclam").EnumerateObject())
        {
            var pair = MainlineTable.Elements(marker.Value);
            if (pair.Length != 2 || pair.Any(value => value.ValueKind != JsonValueKind.Number)) return false;
            if (Player.City.FindArea(MainlineTable.Number(pair[0])) is null) return false;
        }
        return true;
    }

    public void ApplyEvent(EventContentData row)
    {
        foreach (int id in row.Ints("notepadId"))
        {
            if (!Comp.Notepads.Contains(id)) Comp.Notepads.Add(id);
        }
        foreach (int id in row.Ints("privateMsgId")) Player.Social.AddMessage(id, true);
        foreach (int id in row.Ints("socialMsgId")) Player.Social.AddMessage(id, false);
        foreach (var pair in row.Map("ban_heroes")) Player.HeroMgr.SetBanned(pair.Key, MainlineTable.Number(pair.Value));
        foreach (int id in row.Ints("recoverBanHeroes")) Player.HeroMgr.SetBanned(id, 0);
        Player.HeroMgr.LockCinematic(row.Ints("cineLockHeroes"));
        if (row.Get("exclam").ValueKind == JsonValueKind.Object)
        {
            foreach (var marker in row.Get("exclam").EnumerateObject())
            {
                var pair = MainlineTable.Elements(marker.Value);
                if (pair.Length != 2 || pair[0].ValueKind != JsonValueKind.Number) throw new NotSupportedException("暂未支持该区域标记");
                int area = MainlineTable.Number(pair[0]);
                var values = CombatLogic.ReadJson(Comp.AreaExclamJson.GetValueOrDefault(area, "{}"));
                int before = values.TryGetValue(marker.Name, out var value) ? Convert.ToInt32(value) : 0;
                values[marker.Name] = Math.Max(0, before + MainlineTable.Number(pair[1]));
                Comp.AreaExclamJson[area] = JsonSerializer.Serialize(values);
                Notify("syncAreaExclam", new() { ["ae"] = AreaExclam(), ["a"] = area });
            }
        }
        foreach (int id in row.Ints("intelligence")) Player.Intelligence.Add(id);
        if (row.Has("cityMusic")) Notify("changeCityMusic", new() { ["music"] = row.Int("cityMusic") });
        if (row.Has("cityTheme")) Notify("changeCityTheme", new() { ["theme"] = row.Int("cityTheme") });
        if (row.Has("enterStoryRoute") && !Enter(row.Text("enterStoryRoute"))) throw new InvalidOperationException("暂未实现该剧情线");
        if (row.Flag("isLeaveEmergencyEvent")) Player.Status.SetEmergency(false);
        if (row.Has("weekResult")) Player.WeekNum.Conclude(row.Int("weekResult"));
        MarkDirty();
    }

    public void CompleteChapter()
    {
        Comp.ClientEventFinish = false;
        MarkDirty();
    }

    private Dictionary<int, object> AreaExclam() => Comp.AreaExclamJson.ToDictionary(pair => pair.Key, pair => (object)CombatLogic.ReadJson(pair.Value));

    public void Reset()
    {
        Comp.Route = "1";
        Comp.Notepads.Clear();
        Comp.AreaExclamJson.Clear();
        Comp.CurrentCgs.Clear();
        Comp.ClickedRoutes.Clear();
        Comp.GeneralValue = 0;
        Comp.Place = 0;
        Comp.ClientEventFinish = true;
        MarkDirty();
    }
}
