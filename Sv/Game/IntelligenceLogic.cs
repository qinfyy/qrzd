using Sv.Database;
using MongoDB.Bson;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class IntelligenceLogic(Player player) : PlayerLogicBase(player)
{
    private IntelligenceComp Comp => Player.SaveData.IntelligenceComp;

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["rd"] = Readed,
        ["is"] = Comp.Entries.Select(entry => (object)new Dictionary<string, object>
        {
            ["id"] = entry.Id, ["uuid"] = new Dictionary<string, string> { ["$oid"] = entry.Uuid }, ["day"] = entry.Day,
            ["hd"] = entry.State == 1, ["hdd"] = entry.HandledDay, ["ep"] = entry.State == 2, ["op"] = entry.Opened,
            ["ld"] = Math.Max(-1, entry.Day + (GameTableCatalog.Instance.GetDataById<IntelligenceData>(entry.Id)?.Int("lastDay", 1) ?? 1) - Player.WeekNum.Day),
            ["afs"] = entry.Effects.Select(effect => (object)new Dictionary<string, object>
            {
                ["id"] = effect.Id, ["uuid"] = new Dictionary<string, string> { ["$oid"] = effect.Uuid }, ["ld"] = Math.Max(0, effect.Day + 1 - Player.WeekNum.Day),
                ["aid"] = effect.Area, ["bid"] = effect.Building, ["hero"] = effect.Hero, ["rd"] = CombatLogic.ReadJson(effect.RewardJson),
            }).ToArray(),
        }).ToArray(),
    };

    public void Synchronize() => Notify("syncAllIntelligence", new() { ["is"] = ToSnapshot() });

    public bool CanAdd(int id) => GameTableCatalog.Instance.GetDataById<IntelligenceData>(id) is { } row &&
        new[] { "createAffects", "handleAffects", "notHandleAffects", "expireAffects", "autoHandleAffects" }.All(field => row.Ints(field).All(CanEffect));

    private bool CanEffect(int id)
    {
        IntelligenceAffectData? row = GameTableCatalog.Instance.GetDataById<IntelligenceAffectData>(id);
        return row is not null && id is >= 1 and <= 24 && (!row.Has("randomReward") || RewardLogic.CanGrantRandom(Player, row.Ints("randomReward")));
    }

    public bool Add(int id)
    {
        if (!CanAdd(id) || Comp.Entries.Any(entry => entry.Day == Player.WeekNum.Day && entry.Id == id)) return false;
        IntelligenceData row = GameTableCatalog.Instance.GetDataById<IntelligenceData>(id)!;
        IntelligenceState entry = new()
        {
            Id = id, Uuid = ObjectId.GenerateNewId().ToString(), Day = Player.WeekNum.Day, Opened = row.Int("openNeed") == 0, HandledDay = -1,
        };
        Comp.Entries.Add(entry);
        ApplyEffects(entry, row.Ints("createAffects"));
        Readed = false;
        MarkDirty();
        return true;
    }

    public bool Open(string uuid)
    {
        IntelligenceState? entry = Comp.Entries.FirstOrDefault(value => value.Uuid == uuid);
        if (entry is null || entry.State != 0) return false;
        if (entry.Opened) return true;
        int cost = GameTableCatalog.Instance.GetDataById<IntelligenceData>(entry.Id)!.Int("openNeed");
        if (!Player.City.ConsumeIntelligence(cost)) return false;
        entry.Opened = true;
        MarkDirty();
        return true;
    }

    public bool Handle(string uuid)
    {
        IntelligenceState? entry = Comp.Entries.FirstOrDefault(value => value.Uuid == uuid);
        if (entry is null || entry.State == 2 || !entry.Opened || Player.WeekNum.Day >= 7) return false;
        if (entry.State == 1) return true;
        IntelligenceData row = GameTableCatalog.Instance.GetDataById<IntelligenceData>(entry.Id)!;
        if (!CanAdd(entry.Id) || !Player.City.ConsumeIntelligence(row.Int("handleNeed"))) return false;
        entry.State = 1;
        entry.HandledDay = Player.WeekNum.Day;
        ApplyEffects(entry, row.Ints("handleAffects"));
        Synchronize();
        MarkDirty();
        return true;
    }

    public void EndDay()
    {
        if (Comp.EndedDay >= Player.WeekNum.Day) return;
        foreach (IntelligenceState entry in Comp.Entries.Where(value => value.State == 0))
        {
            IntelligenceData row = GameTableCatalog.Instance.GetDataById<IntelligenceData>(entry.Id)!;
            if (entry.Day + row.Int("lastDay", 1) > Player.WeekNum.Day + 1) continue;
            ApplyEffects(entry, row.Ints("notHandleAffects"));
            ApplyEffects(entry, row.Ints("expireAffects"));
            entry.State = 2;
        }
        Comp.EndedDay = Player.WeekNum.Day;
        MarkDirty();
    }

    public void NewDay()
    {
        int day = Player.WeekNum.Day;
        if (Comp.GeneratedDay == day || day >= 7) return;
        IntelligenceRefreshData? row = GameTableCatalog.Instance.GetAllData<IntelligenceRefreshData>().FirstOrDefault(value =>
            value.Int("day") == day && value.Int("week", -1) == Player.WeekNum.Week) ?? GameTableCatalog.Instance.GetAllData<IntelligenceRefreshData>()
            .FirstOrDefault(value => value.Int("day") == day && value.Int("week", -1) == -1);
        if (row is not null)
        {
            foreach (int id in row.Ints("certain")) Add(id);
            foreach (var group in row.Map("refresh"))
            {
                IntelligenceData[] candidates = GameTableCatalog.Instance.GetAllData<IntelligenceData>()
                    .Where(value => value.Int("type") == group.Key && value.Int("weight") > 0 && CanAdd(value.Id))
                    .Where(value => !Comp.Entries.Any(entry => entry.Id == value.Id && entry.State != 2))
                    .Where(value => value.Id is < 1 or > 4 || Player.City.FindArea(9 - value.Id) is { Status: 2 }).ToArray();
                for (int i = 0; i < MainlineTable.Number(group.Value) && candidates.Length > 0; i++)
                {
                    int roll = Random.Shared.Next(candidates.Sum(value => value.Int("weight")));
                    IntelligenceData selected = candidates.First(value => (roll -= value.Int("weight")) < 0);
                    Add(selected.Id);
                    candidates = candidates.Where(value => value.Id != selected.Id).ToArray();
                }
            }
        }
        Comp.GeneratedDay = day;
        Player.City.RefreshIntelligence();
        Synchronize();
        MarkDirty();
    }

    private void ApplyEffects(IntelligenceState entry, IEnumerable<int> ids)
    {
        foreach (int id in ids)
        {
            if (entry.Effects.Any(value => value.Id == id)) continue;
            IntelligenceAffectData row = GameTableCatalog.Instance.GetDataById<IntelligenceAffectData>(id)!;
            IntelligenceEffect effect = new() { Id = id, Uuid = ObjectId.GenerateNewId().ToString(), Day = Player.WeekNum.Day + 1 };
            entry.Effects.Add(effect);
            foreach (int eventId in row.Ints("events")) { Player.EventTrigger.Queue(eventId); Notify("triggerIntelligenceEvents", new() { ["e"] = eventId }); }
            if (id == 6)
            {
                Player.City.DestroyIntelligenceBuilding(out int area, out int building);
                effect.Area = area;
                effect.Building = building;
            }
            if (id == 7 && Player.HeroMgr.HeroIds.Length > 0)
            {
                effect.Hero = Player.HeroMgr.HeroIds[Random.Shared.Next(Player.HeroMgr.HeroIds.Length)];
                Player.HeroMgr.ConsumeFatigue(effect.Hero, Math.Min(20, Player.HeroMgr.Find(effect.Hero)!.Fatigue));
            }
            if (id == 8) Player.HeroMgr.RestoreAllFatigue(-5);
            if (id is 9 or 10) foreach (int hero in Player.HeroMgr.HeroIds) Player.HeroMgr.AddFriendly(hero, id == 9 ? -5 : 5);
            if (id == 15) effect.Area = Player.City.AreaStates().FirstOrDefault(pair => pair.Value == 0).Key;
            if (row.Has("randomReward"))
            {
                var reward = RewardLogic.GrantRandom(Player, row.Ints("randomReward"));
                effect.RewardJson = System.Text.Json.JsonSerializer.Serialize(reward);
                Notify("procRewardSettlement", new() { ["r"] = reward });
            }
        }
        Player.HeroMgr.Synchronize();
        MarkDirty();
    }

    public int PatrolPenalty(int area) => Comp.Entries.SelectMany(entry => entry.Effects).Any(effect => effect.Id == 15 && effect.Area == area &&
        effect.Day == Player.WeekNum.Day) ? 6 : 0;

    public Dictionary<string, object> CombatModifiers()
    {
        double rate = Comp.Entries.SelectMany(entry => entry.Effects).Where(effect => effect.Day == Player.WeekNum.Day)
            .Sum(effect => effect.Id == 5 ? 0.15 : effect.Id == 23 ? 0.05 : 0);
        return new() { ["hurt_addition_rate"] = rate, ["max_hp_addition_rate"] = rate };
    }

    public void Reset()
    {
        Comp.Entries.Clear();
        Comp.GeneratedDay = Comp.EndedDay = -1;
        Comp.Readed = false;
        MarkDirty();
    }

    public bool Readed
    {
        get => Comp.Readed;
        set
        {
            if (Comp.Readed != value)
            {
                Comp.Readed = value;
                MarkDirty();
            }
        }
    }
}
