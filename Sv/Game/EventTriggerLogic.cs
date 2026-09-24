using System.Text.Json;
using Serilog;
using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class EventTriggerLogic(Player player) : PlayerLogicBase(player)
{
    private static readonly HashSet<string> SupportedFields =
    ["completeConds", "completeEnterMainPanel", "contentFinishBattle", "contentFinishUI", "dialog", "emergencyEvent", "enterBattle", "eventType",
        "isAutoTriggerEvent", "isEmergencyEvent", "isLeaveEmergencyEvent", "mutexEvent", "name", "newbee", "priority", "priorityGroup", "release", "repeatable",
        "events", "autoStart", "notepadId", "rewards", "newbee_event", "city_attention", "ui", "patrolEvent", "weekResult", "areaFree", "areaFight", "areaFall",
        "areaLock", "areaUnlock", "areaCR7", "changeBlackcore", "batchChangeBlackcore", "hiddenBuilding", "complete_newbee_patrol", "enterStoryRoute", "privateMsgId",
        "socialMsgId", "cityMusic", "cityTheme", "buildBuilding", "destroyPatrolBuild", "destroyAreaPatrolBuild", "refreshMission", "ban_heroes", "recoverBanHeroes",
        "cineLockHeroes", "rel_heroes", "exclam", "intelligence"];
    private static readonly ILogger Logger = Log.ForContext<EventTriggerLogic>();
    private EventTriggerComp Comp => Player.SaveData.EventTriggerComp;
    private bool _refreshing;
    public IReadOnlyList<int> Completed => Comp.CompletedEvents;
    public int[] Processing => Comp.ProcessingEvents.Select(value => value.EventId).ToArray();
    public IReadOnlyList<EventChoice> Choices => Comp.Choices;
    public bool IsCompleted(int id) => Comp.CompletedEvents.Contains(id);
    public EventProgress? Find(int id) => Comp.ProcessingEvents.FirstOrDefault(value => value.EventId == id);

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["ae"] = AvailableSnapshot(), ["pe"] = Processing, ["ecm"] = Comp.CompletedEvents.ToArray(),
        ["gle"] = Comp.LockedEvents.ToArray(), ["ecd"] = new Dictionary<string, object>(),
    };

    public object[] AvailableSnapshot() => Available().Select(row => (object)new object[] { row.Id, 1 }).ToArray();

    public EventContentData[] Available(bool includePatrol = true)
    {
        if (Player.WeekNum.Week != 0 || Player.WeekNum.EndingId != 0 || Player.Story.Route != "1") return [];
        HashSet<int> ids = Comp.QueuedEvents.ToHashSet();
        foreach (EventConditionData condition in GameTableCatalog.Instance.GetAllData<EventConditionData>())
        {
            if (!condition.Flag("release") || condition.Unsupported.Count != 0) continue;
            if (condition.Rules.All(pair => pair.Value.Matches(ConditionValue(pair.Key)))) ids.Add(condition.Id);
        }
        return ids.Select(id => GameTableCatalog.Instance.GetDataById<EventContentData>(id))
            .OfType<EventContentData>()
            .Where(row => IsSupported(row) && Find(row.Id) is null && (!IsCompleted(row.Id) || CanRepeat(row)) && !Comp.LockedEvents.Contains(row.Id))
            .Where(row => includePatrol || !row.Patrol)
            .OrderBy(row => row.Int("priority", 5)).ThenBy(row => row.Id).ToArray();
    }

    public bool IsSupported(EventContentData row)
    {
        if (!row.Released || row.Flag("client_event")) return false;
        if (row.Fields.Keys.Any(key => !SupportedFields.Contains(key))) return false;
        if (row.Has("enterStoryRoute") && row.Text("enterStoryRoute") != "1") return false;
        if (row.Has("weekResult") && row.Int("weekResult") is not (1 or 2 or 6 or 7)) return false;
        if (!row.Ints("privateMsgId").All(id => Player.Social.CanAddMessage(id, true))) return false;
        if (!row.Ints("socialMsgId").All(id => Player.Social.CanAddMessage(id, false))) return false;
        if (row.CompletionConditions.Keys.Any(kind => kind is not ("dialog" or "battle" or "ui"))) return false;
        return row.Int("eventType", 1) is 1 or 2 or 4 or 5 && RewardLogic.CanGrant(Player, row.Ints("rewards")) &&
            Player.City.CanApplyEvent(row) && Player.Story.CanApplyEvent(row);
    }

    private bool CanRepeat(EventContentData row) => row.RepeatableEvent && row.Patrol && Player.City.PatrolArea != 0 &&
        Comp.CompletionActions.GetValueOrDefault(row.Id, -1) < Player.City.TotalActions;

    public string[] BlockingConditions(int eventId)
    {
        if (!GameTableCatalog.Instance.TryGetDataById<EventConditionData>(eventId, out var row)) return ["只可由前置剧情、消息或战斗触发"];
        List<string> blocked = row.Unsupported.Select(pair => $"条件 {pair.Key} 不支持: {pair.Value}").ToList();
        foreach ((int kind, ResourceCondition rule) in row.Rules)
        {
            object? actual = ConditionValue(kind);
            if (!rule.Matches(actual)) blocked.Add($"条件 {kind}: 需要 {rule.Expression}，当前 {JsonSerializer.Serialize(actual)}");
        }
        if (IsCompleted(eventId)) blocked.Add("本周已完成");
        if (Comp.LockedEvents.Contains(eventId)) blocked.Add("互斥分支已锁定");
        return blocked.ToArray();
    }

    public bool HasBlockingEvents() => Processing.Length > 0 || Available(false).Any(row => row.Flag("isAutoTriggerEvent") || row.Emergency);

    public void Refresh(bool push = true)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!Player.Newbee.Enabled)
            {
                foreach (int id in Processing)
                    if (GameTableCatalog.Instance.GetDataById<EventContentData>(id) is { } row && IsStandaloneTutorial(row)) SkipTutorialEvent(id);
            }
            for (int i = 0; i < 256; i++)
            {
                EventContentData? automatic = Available().FirstOrDefault(row => !row.Patrol &&
                    (row.Flag("autoStart") && row.IsPresentationOnly || !Player.Newbee.Enabled && IsStandaloneTutorial(row)));
                if (automatic is null) break;
                if (!Player.Newbee.Enabled && IsStandaloneTutorial(automatic))
                {
                    SkipTutorialEvent(automatic.Id);
                    continue;
                }
                Begin(automatic);
                Complete(automatic, Find(automatic.Id)!);
            }
            if (push) Push();
        }
        finally { _refreshing = false; }
    }

    private static bool IsStandaloneTutorial(EventContentData row) => row.Int("newbee_event") > 0 && row.Battle == 0 && row.NextEvents.Length == 0 &&
        row.Ints("contentFinishBattle").Length == 0 && row.CompletionConditions.Keys.All(kind => kind == "dialog") &&
        (GameTableCatalog.Instance.GetDataById<EventDialogueData>(row.DialogueId) is not { } dialog || dialog.List("opts").Length == 0 && dialog.Int("finish_event") == 0);

    internal void SkipTutorialEvent(int eventId)
    {
        EventContentData? row = GameTableCatalog.Instance.GetDataById<EventContentData>(eventId);
        if (Player.Newbee.Enabled || row is null || !(row.Flag("newbee") || eventId is 10000 or 1041 or 1047 || IsStandaloneTutorial(row)))
            throw new InvalidOperationException($"不可跳过非引导剧情 {eventId}");
        if (IsCompleted(eventId))
        {
            if (Find(eventId) is { } processing) Comp.ProcessingEvents.Remove(processing);
            Comp.QueuedEvents.Remove(eventId);
            MarkDirty();
            return;
        }
        if (!CanComplete(row)) throw new InvalidOperationException($"引导剧情配置暂不支持 {eventId}");
        Comp.QueuedEvents.Remove(eventId);
        EventProgress progress = Find(eventId) ?? new EventProgress { EventId = eventId, StartedDay = Player.WeekNum.Day };
        if (!Comp.ProcessingEvents.Contains(progress)) Comp.ProcessingEvents.Add(progress);
        Complete(row, progress);
    }

    public void Push() => Notify("pushEvents", new() { ["a"] = AvailableSnapshot(), ["p"] = Processing });

    public bool Start(int eventId)
    {
        EventContentData? row = GameTableCatalog.Instance.GetDataById<EventContentData>(eventId);
        if (row is null || !IsSupported(row)) return false;
        if (Find(eventId) is null)
        {
            if (!Available().Any(value => value.Id == eventId)) return false;
            Begin(row);
        }
        Notify("startEventReply", new() { ["e"] = eventId, ["r"] = 1 });
        TryComplete(row, Find(eventId)!);
        return true;
    }

    private void Begin(EventContentData row)
    {
        Comp.QueuedEvents.Remove(row.Id);
        Comp.ProcessingEvents.Add(new EventProgress { EventId = row.Id, StartedDay = Player.WeekNum.Day });
        if (row.Ints("contentFinishBattle").Length > 0) Player.City.RefreshStages();
        if (row.Battle > 0) Notify("setEventBattleStage", new() { ["s"] = row.Battle, ["n"] = 1 });
        if (row.DialogueId == 0 && row.Int("newbee_event") > 0) Player.Newbee.Request(row.Int("newbee_event"));
        MarkDirty();
        Logger.Information("City UID={Uid} 开始事件 {Event} {Name}", Player.Uid, row.Id, row.Text("name"));
    }

    public bool SelectOption(int eventId, int selectedEvent, int option)
    {
        EventProgress? progress = Find(eventId);
        EventContentData? row = GameTableCatalog.Instance.GetDataById<EventContentData>(eventId);
        EventContentData? next = GameTableCatalog.Instance.GetDataById<EventContentData>(selectedEvent);
        if (progress is null || row is null || next is null || !IsSupported(next) || !row.NextEvents.Contains(selectedEvent) || Comp.LockedEvents.Contains(selectedEvent)) return false;
        if (Find(selectedEvent) is not null || Player.Combat.IsActive) return false;
        if (!GameTableCatalog.Instance.TryGetDataById<EventDialogueData>(row.DialogueId, out var dialog) || !dialog.AllowsOption(selectedEvent, option)) return false;
        if (row.CompletionConditions.ContainsKey("battle")) return false;
        if (!CanComplete(row)) return false;
        Comp.Choices.Add(new EventChoice { EventId = eventId, SelectedEvent = selectedEvent, Option = option, Day = Player.WeekNum.Day });
        if (!progress.Dialogs.Contains(row.DialogueId)) progress.Dialogs.Add(row.DialogueId);
        Complete(row, progress);
        // Dialogue menus may revisit an already-read question, but its reward remains one-shot.
        Begin(next);
        Notify("startEventReply", new() { ["e"] = selectedEvent, ["r"] = 1 });
        TryComplete(next, Find(selectedEvent)!);
        MarkDirty();
        return true;
    }

    public bool Trace(string kind, int value)
    {
        if (kind != "dialog") return false;
        EventProgress? progress = Comp.ProcessingEvents.FirstOrDefault(value1 =>
            GameTableCatalog.Instance.GetDataById<EventContentData>(value1.EventId)?.DialogueId == value);
        if (progress is null) return false;
        EventContentData row = GameTableCatalog.Instance.GetDataById<EventContentData>(progress.EventId)!;
        if (GameTableCatalog.Instance.GetDataById<EventDialogueData>(value) is { } dialog && (dialog.List("opts").Length > 0 || dialog.Int("finish_event") > 0))
            return false;
        if (!progress.Dialogs.Contains(value)) { progress.Dialogs.Add(value); MarkDirty(); }
        TryComplete(row, progress);
        return true;
    }

    public bool TraceUi(string value)
    {
        EventProgress? progress = Comp.ProcessingEvents.FirstOrDefault(entry =>
            GameTableCatalog.Instance.GetDataById<EventContentData>(entry.EventId)!.List("contentFinishUI").Any(ui => ui.GetString() == value));
        if (progress is null) return false;
        if (!progress.Ui.Contains(value)) progress.Ui.Add(value);
        MarkDirty();
        TryComplete(GameTableCatalog.Instance.GetDataById<EventContentData>(progress.EventId)!, progress);
        return true;
    }

    private void TryComplete(EventContentData row, EventProgress progress)
    {
        foreach ((string key, JsonElement condition) in row.CompletionConditions)
        {
            int[] required = MainlineTable.Elements(condition).Select(value => MainlineTable.Number(value)).ToArray();
            if (key == "dialog" && required.All(progress.Dialogs.Contains)) continue;
            if (key == "battle" && required.All(progress.Battles.Contains)) continue;
            if (key == "ui" && MainlineTable.Elements(condition).All(value => progress.Ui.Contains(value.GetString()))) continue;
            return;
        }
        if (CanComplete(row)) Complete(row, progress);
    }

    private bool CanComplete(EventContentData row) => IsSupported(row) && (!row.Has("weekResult") || Player.WeekNum.CanConclude(row.Int("weekResult")));

    private void Complete(EventContentData row, EventProgress progress)
    {
        if (!Comp.ProcessingEvents.Contains(progress)) return;
        bool first = !IsCompleted(row.Id);
        Dictionary<string, object> rewards = [];
        if (first || CanRepeat(row))
        {
            rewards = RewardLogic.Grant(Player, row.Ints("rewards"));
            if (first) Comp.CompletedEvents.Add(row.Id);
            Comp.CompletionDays[row.Id] = Player.WeekNum.Day;
            Comp.CompletionActions[row.Id] = Player.City.TotalActions;
            foreach (int excluded in row.Ints("mutexEvent"))
            {
                if (!Comp.LockedEvents.Contains(excluded)) Comp.LockedEvents.Add(excluded);
                Comp.QueuedEvents.Remove(excluded);
            }
            Player.City.ApplyEvent(row);
            Player.Story.ApplyEvent(row);
        }
        Comp.ProcessingEvents.Remove(progress);
        MarkDirty();
        if (rewards.Count > 0) Notify("completeEventReplyStart", new() { ["e"] = row.Id, ["r"] = rewards });
        Notify("completeEventReplyEnd", new() { ["e"] = row.Id });
        if (row.Battle > 0) Notify("setEventBattleStage", new() { ["s"] = row.Battle, ["n"] = 0 });
        if (row.DialogueId > 0 && row.Int("newbee_event") > 0) Player.Newbee.Request(row.Int("newbee_event"));
        Player.City.Synchronize();
        Player.Newbee.Synchronize();
        Logger.Information("City UID={Uid} 完成事件 {Event} {Name} 首次={First}", Player.Uid, row.Id, row.Text("name"), first);
    }

    public void Queue(int eventId)
    {
        EventContentData? row = GameTableCatalog.Instance.GetDataById<EventContentData>(eventId);
        if (row is null || !IsSupported(row) || Comp.LockedEvents.Contains(eventId) || Find(eventId) is not null) return;
        if (IsCompleted(eventId))
        {
            if (!CanRepeat(row)) return;
        }
        if (!Comp.QueuedEvents.Contains(eventId)) { Comp.QueuedEvents.Add(eventId); MarkDirty(); }
    }

    public int EventForBattle(int stage) => Comp.ProcessingEvents.FirstOrDefault(progress =>
        GameTableCatalog.Instance.GetDataById<EventContentData>(progress.EventId)?.Battle == stage)?.EventId ?? 0;

    public bool CanQueue(int eventId) => GameTableCatalog.Instance.GetDataById<EventContentData>(eventId) is { } row && IsSupported(row) && !Comp.LockedEvents.Contains(eventId);

    public bool AllowsStage(int stage) => Comp.ProcessingEvents.All(progress =>
        GameTableCatalog.Instance.GetDataById<EventContentData>(progress.EventId)?.Ints("contentFinishBattle").Contains(stage) == true);

    public void BattleFinished(MissionData mission, bool win)
    {
        if (win && !Comp.BattleCompletions.Contains(mission.Id)) Comp.BattleCompletions.Add(mission.Id);
        foreach (EventProgress progress in Comp.ProcessingEvents.ToArray())
        {
            EventContentData row = GameTableCatalog.Instance.GetDataById<EventContentData>(progress.EventId)!;
            if (!row.Ints("contentFinishBattle").Contains(mission.Id)) continue;
            if (!win && (mission.Int("system_type") == 1 || mission.Ints("failEvent").Length == 0)) continue;
            if (!progress.Battles.Contains(mission.Id)) progress.Battles.Add(mission.Id);
            TryComplete(row, progress);
        }
        foreach (int id in mission.Ints(win ? "successEvent" : "failEvent")) Queue(id);
        MarkDirty();
    }

    public bool UpdateClientCondition(int type, JsonElement value)
    {
        switch (type)
        {
            case 100:
                if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 20) return false;
                string clicked = value.GetString()!;
                if (!int.TryParse(clicked.Split(':')[0], out int area) || Player.City.FindArea(area) is null) return false;
                Comp.ClickedArea = clicked;
                break;
            case 101:
                int emergency = MainlineTable.Number(value, -1);
                if (emergency is not (0 or 1)) return false;
                if ((emergency == 1) != Player.Status.Emergency) return false;
                break;
            case 102:
                int[] operations = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => MainlineTable.Number(v)).ToArray() : [MainlineTable.Number(value)];
                if (operations.Length > 6 || operations.Any(op => op is < 1 or > 6)) return false;
                foreach (int operation in operations)
                {
                    if (operation == 2 && Player.City.PatrolArea == 0) continue;
                    if (operation == 1 && Player.WeekNum.Day != 7) continue;
                    if (operation == 4 && !Player.City.PrepareNextDay()) return false;
                    if (!Comp.ClientOperations.Contains(operation)) Comp.ClientOperations.Add(operation);
                }
                break;
            default: return false;
        }
        MarkDirty();
        return true;
    }

    public void SetOperation(int operation, bool enabled)
    {
        if (enabled && !Comp.ClientOperations.Contains(operation)) Comp.ClientOperations.Add(operation);
        if (!enabled) Comp.ClientOperations.Remove(operation);
        MarkDirty();
    }

    public void NewDay()
    {
        Comp.ClientOperations.Clear();
        Comp.ClickedArea = "";
        MarkDirty();
    }

    public Dictionary<string, object> ToBattleSnapshot() => new()
    {
        ["ab"] = Comp.ProcessingEvents.Select(progress => GameTableCatalog.Instance.GetDataById<EventContentData>(progress.EventId)!.Battle)
            .Where(stage => stage != 0).Distinct().Select(stage => (object)new[] { stage, 1 }).ToArray(),
        ["psh"] = Comp.BattleCompletions.Select(stage => (object)new[] { stage, 1 }).ToArray(),
    };

    public Dictionary<string, object> OptionSnapshot(int eventId) => new()
    {
        ["e"] = eventId, ["d"] = Comp.Choices.Where(choice => choice.EventId == eventId).GroupBy(choice => choice.Option)
            .Select(group => new[] { group.Key, group.Count() }).ToArray(),
    };

    public void Reset()
    {
        Comp.CompletedEvents.Clear();
        Comp.ProcessingEvents.Clear();
        Comp.QueuedEvents.Clear();
        Comp.LockedEvents.Clear();
        Comp.Choices.Clear();
        Comp.BattleCompletions.Clear();
        Comp.CompletionActions.Clear();
        Comp.CompletionDays.Clear();
        NewDay();
    }

    private object? ConditionValue(int kind)
    {
        return kind switch
        {
            1 or 11 or 17 => Comp.CompletedEvents.ToArray(),
            3 => Player.City.PatrolArea,
            4 => Player.WeekNum.Day,
            5 => Player.Inventory.Counts(),
            6 => Player.HeroMgr.HeroIds.ToDictionary(id => id, id => Player.HeroMgr.Find(id)!.Friendly),
            7 => Player.Profile.Level,
            8 or 47 => Player.City.AreaStates(),
            9 => Comp.BattleCompletions.ToArray(),
            10 => Player.City.PatrolHeroes,
            12 => Player.City.DailyConsumedAction,
            13 => Player.HeroMgr.HeroIds.Sum(Player.HeroMgr.GetInsightValue),
            14 => Player.HeroMgr.HeroIds.Sum(Player.HeroMgr.GetConstructValue),
            15 => Player.HeroMgr.HeroIds.Sum(Player.HeroMgr.GetLeadershipValue),
            16 => Player.WeekNum.Week,
            18 => Player.City.AreaBuildings(),
            19 => Comp.CompletionDays.ToDictionary(pair => pair.Key, pair => Player.WeekNum.Day - pair.Value),
            20 => Comp.CompletionActions.ToDictionary(pair => pair.Key, pair => Player.City.TotalActions - pair.Value),
            21 => Player.City.BuildFund,
            22 => Player.City.AreaLevels(),
            23 => Player.City.BlackcoreCounts(),
            24 or 25 => Player.HeroMgr.HeroIds,
            26 => Player.City.ForceVal,
            27 => Player.City.ResearchVal,
            28 => Player.City.DevelopVal,
            29 => Player.City.FindArea(Player.City.PatrolArea)?.Buildings.Select(building => building.BuildingId).ToArray() ?? [],
            30 => Player.City.CenterConfidence,
            31 => Player.HeroMgr.HeroIds.ToDictionary(id => id, id => Player.HeroMgr.Find(id)!.StarLevel),
            33 => Player.Profile.Money,
            37 => Player.HeroMgr.HeroIds.ToDictionary(id => id, id => Player.HeroMgr.Find(id)!.Fatigue),
            38 => Player.City.PatrolHeroes.Sum(Player.HeroMgr.GetInsightValue),
            39 => Player.City.PatrolHeroes.Sum(Player.HeroMgr.GetConstructValue),
            40 => Player.City.PatrolHeroes.Sum(Player.HeroMgr.GetLeadershipValue),
            42 or 48 => Player.Story.Cgs.ToArray(),
            46 => Player.HeroMgr.HeroIds.Count(id => Player.HeroMgr.Find(id)!.Friendly >= 50),
            49 => Player.HeroMgr.HeroIds.Count(id => Player.HeroMgr.Find(id)!.Fatigue < 20),
            52 => Player.Story.Route,
            54 => Player.Story.GeneralValue,
            100 => Comp.ClickedArea,
            101 => Player.Status.Emergency ? 1 : 0,
            102 => Comp.ClientOperations.ToArray(),
            _ => null,
        };
    }
}
