using Sv.Database;
using System.Text.Json;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class WeekNumLogic(Player player) : PlayerLogicBase(player)
{
    private WeekNumComp Comp => Player.SaveData.WeekNumComp;
    private bool _resumeEnding;

    public int EndingId => Comp.EndingId;
    public bool ChapterPending => Comp.ChapterPending;
    private DailySettlement? Pending => Comp.Settlements.LastOrDefault(value => !value.Acknowledged);

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["w"] = Week, ["d"] = Day, ["c"] = Pending is { } pending ? CombatLogic.ReadJson(pending.SnapshotJson) : new Dictionary<string, object>(),
        ["l"] = PreviousDaily(),
        ["wasi"] = EndingId == 0 ? Array.Empty<object>() : new object[] { new object[] { Week, CombatLogic.ReadJson(Comp.ScoreJson) } },
    };

    protected internal override void OnLogin() => _resumeEnding = ChapterPending;

    public void ResumeEnding()
    {
        if (!_resumeEnding || !ChapterPending) return;
        ReplayEnding();
        _resumeEnding = false;
    }

    private Dictionary<string, object> PreviousDaily()
    {
        int day = Pending?.Day ?? Day;
        DailySettlement? previous = Comp.Settlements.LastOrDefault(value => value.Week == Week && value.Day < day);
        return previous is null ? DailySnapshot() : CombatLogic.ReadJson(previous.SnapshotJson);
    }

    private Dictionary<string, object> DailySnapshot() => new()
    {
        ["rsv"] = Player.City.ResearchVal, ["dvv"] = Player.City.DevelopVal, ["frv"] = Player.City.ForceVal,
        ["d"] = Day, ["w"] = Week,
    };

    public string CanAdvance()
    {
        if (Week != 0 || Day >= 7 || EndingId != 0) return "ending";
        if (Player.Combat.IsActive) return "battle";
        if (Player.Status.Emergency || Player.EventTrigger.HasBlockingEvents()) return "event";
        return "ok";
    }

    public bool Advance()
    {
        if (Pending is not null) { ReplaySettlement(); return true; }
        if (CanAdvance() != "ok" || Player.City.ActionVal != 0) return false;
        Player.Intelligence.EndDay();
        Player.EventTrigger.Refresh();
        if (Player.EventTrigger.HasBlockingEvents()) return false;
        DailySettlement settlement = new() { Week = Week, Day = Day, SnapshotJson = JsonSerializer.Serialize(DailySnapshot()) };
        Comp.Settlements.Add(settlement);
        Player.City.EndActions();
        Day++;
        Player.City.NewDay(Day == 7);
        FatigueRecoveryData? recovery = GameTableCatalog.Instance.GetDataById<FatigueRecoveryData>(Week);
        Player.HeroMgr.RestoreAllFatigue(recovery?.Int("fatigue_recover", 5) ?? 5);
        Player.EventTrigger.NewDay();
        Player.Intelligence.NewDay();
        Player.City.Synchronize(true);
        ReplaySettlement();
        Player.EventTrigger.Refresh();
        MarkDirty();
        return true;
    }

    public void ReplaySettlement()
    {
        if (Pending is { } pending) Notify("showDailySettlement", new() { ["c"] = CombatLogic.ReadJson(pending.SnapshotJson), ["l"] = PreviousDaily() });
        Notify("updateDay", new() { ["d"] = Day });
    }

    public void ClearSettlement()
    {
        if (Pending is not { } pending) return;
        pending.Acknowledged = true;
        MarkDirty();
    }

    public bool CanConclude(int ending) => ending is 1 or 2 or 6 or 7 && Day == 7 && Week == 0 && (EndingId == 0 || EndingId == ending);

    public void Conclude(int ending)
    {
        if (!CanConclude(ending)) throw new InvalidOperationException("当前不能进入该结局");
        if (EndingId != 0) return;
        EndingData row = GameTableCatalog.Instance.GetDataById<EndingData>(ending)!;
        if (!Player.Story.CanAddCg(row.Int("cg"))) throw new InvalidOperationException("结局 CG 奖励暂不支持");
        Comp.EndingId = ending;
        Comp.ChapterPending = true;
        Player.Story.AddEnding(ending);
        Player.Story.AddCg(row.Int("cg"));
        Player.Story.CompleteChapter();
        Comp.ScoreJson = JsonSerializer.Serialize(CalculateScore());
        Comp.EndingRewardJson = JsonSerializer.Serialize(new Dictionary<string, object> { ["cg"] = new[] { row.Int("cg") } });
        Notify("updateStoryFlag", new() { ["f"] = false });
        ReplayEnding();
        MarkDirty();
    }

    public void ReplayEnding()
    {
        if (EndingId == 0) return;
        Notify("syncWeeknumScoreInfo", new() { ["wsi"] = CombatLogic.ReadJson(Comp.ScoreJson), ["ls"] = Array.Empty<object>() });
        Notify("showWeekResult", new() { ["r"] = EndingId, ["rd"] = CombatLogic.ReadJson(Comp.EndingRewardJson) });
    }

    private Dictionary<string, object> CalculateScore()
    {
        Dictionary<string, object> result = new()
        {
            ["name"] = Player.Profile.NickName, ["week"] = Week, ["ending"] = EndingId, ["cur_cgs"] = Player.Story.CurrentCgs,
        };
        int total = 0;
        foreach (WeekendScoreData row in GameTableCatalog.Instance.GetAllData<WeekendScoreData>())
        {
            int score = 0;
            int count = 0;
            int[] conditions = row.Ints("cond");
            foreach (int id in conditions)
            {
                WeekendConditionData? condition = GameTableCatalog.Instance.GetDataById<WeekendConditionData>(id);
                if (condition is null) continue;
                if (id == 12)
                {
                    count = Player.Story.CurrentCgs.Length;
                    score = Player.Story.CurrentCgs.Sum(cg => GameTableCatalog.Instance.GetDataById<CgData>(cg)?.Int("score") ?? 0);
                }
                else if (id == 9)
                {
                    count = Player.City.ForceVal + Player.City.ResearchVal + Player.City.DevelopVal;
                    score = GameTableCatalog.Instance.GetAllData<WeekendCityScoreData>().Where(value => value.Id <= count).Max(value => value.Int("score"));
                }
                else if (id == 13)
                {
                    var wins = Player.Combat.Settled.Where(battle => battle.Win == 1).Select(battle => battle.Stage).Distinct()
                        .Select(stage => GameTableCatalog.Instance.GetDataById<MissionData>(stage)!).Where(stage => stage.Int("battle_win_score") > 0).ToArray();
                    score = wins.Sum(stage => stage.Int("battle_win_score"));
                    count = wins.Length;
                }
                else if ((condition.Has("open_area") && condition.Ints("open_area").All(area => Player.City.FindArea(area)?.Status == 0)) ||
                    (condition.Has("finish_event") && condition.Ints("finish_event").All(Player.EventTrigger.IsCompleted)))
                {
                    score += condition.Int("score");
                    count++;
                }
            }
            result[row.Id.ToString()] = new[] { score, count, conditions.Length };
            total += score;
        }
        result["total"] = total;
        return result;
    }

    public void Reset()
    {
        Comp.Week = Comp.Day = Comp.EndingId = 0;
        Comp.ChapterPending = false;
        Comp.ScoreJson = Comp.EndingRewardJson = "";
        _resumeEnding = false;
        Comp.Settlements.Clear();
        MarkDirty();
    }

    public void ResetMainline()
    {
        Reset();
        Player.EventTrigger.Reset();
        Player.Combat.Reset();
        Player.Story.Reset();
        Player.Newbee.Reset();
        Player.Social.Reset();
        Player.Intelligence.Reset();
        Player.HeroMgr.ResetStoryHeroes();
        Player.City.Reset();
        Player.Status.Reset();
        Player.Rpc.Reset();
        Player.Newbee.ApplyConfiguration();
    }

    public int Week
    {
        get => Comp.Week;
        set
        {
            if (Comp.Week != value)
            {
                Comp.Week = value;
                MarkDirty();
            }
        }
    }

    public int Day
    {
        get => Comp.Day;
        set
        {
            if (Comp.Day != value)
            {
                Comp.Day = value;
                MarkDirty();
            }
        }
    }
}
