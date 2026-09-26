using System.Text.Json;
using MongoDB.Bson;
using Sv.Game;
using static Sv.Gateway.Handlers.MainlineHandlers;

namespace Sv.Gateway.Handlers;

public static class StoryHandlers
{
    public static readonly HashSet<string> Methods = ["pullEvents", "reliableEcho", "startEvent", "startEventOption", "clientTraceProcessingEvent",
        "updateEventCondClient", "queryEventOptAsk", "statusSwitch", "statusCheck", "enterPlace", "tutorialBegin", "updateStoryClickFlag",
        "requestEnterStory", "clientAskTriggerNewbee", "clientFinishNewbee", "passInitPrologue", "checkProcessingEvent", "requestLuckyDrawFile",
        "requestLuckyDrawCard", "requestFile", "requestQuitFile", "triggerExternalEvent"];

    public static void OnRequest(Player player, string method, BsonDocument args)
    {
        switch (method)
        {
            case "reliableEcho": break;
            // 客户端引导（含深夜食堂 1046）主动上报外部事件，服务端只负责应答解锁 UI。
            case "triggerExternalEvent": player.Notify("triggerExternalEventReply", new Dictionary<string, object>()); break;
            case "pullEvents":
                player.EventTrigger.Refresh(false);
                player.Notify("pullEventsReply", new() { ["a"] = player.EventTrigger.AvailableSnapshot(), ["p"] = player.EventTrigger.Processing });
                player.WeekNum.ResumeEnding();
                break;
            case "startEvent": Require(player.EventTrigger.Start(Int(args, "e")), "剧情当前不可开启"); break;
            case "startEventOption": Require(player.EventTrigger.SelectOption(Int(args, "e"), Int(args, "se"), Int(args, "op")), "剧情选项无效"); break;
            case "clientTraceProcessingEvent":
                BsonValue value = args["d"].AsBsonDocument.GetValue("c", BsonNull.Value);
                Require(args["k"].AsString == "ui" ? player.EventTrigger.TraceUi(value.ToInt32()) : player.EventTrigger.Trace(args["k"].AsString, value.ToInt32()));
                break;
            case "updateEventCondClient":
                using (JsonDocument json = JsonDocument.Parse(args["v"].AsString)) Require(player.EventTrigger.UpdateClientCondition(Int(args, "c"), json.RootElement));
                break;
            case "queryEventOptAsk": player.Notify("queryEventOptReply", player.EventTrigger.OptionSnapshot(Int(args, "e"))); break;
            case "statusSwitch":
            case "statusCheck":
                string status = args["s"].AsString;
                string result = method == "statusSwitch" ? player.Status.Switch(status) : player.Status.Check(status);
                player.Notify(method + "Reply", new() { ["r"] = result, ["s"] = status });
                break;
            case "enterPlace": Require(player.Story.SetPlace(Int(args, "p"))); break;
            case "tutorialBegin": player.Newbee.BeginTutorial(); break;
            case "updateStoryClickFlag": player.Story.ClickRoute(args["r"].ToString()!); break;
            case "requestEnterStory":
                Require(player.Story.Enter(args["i"].ToString()!), "仅支持首周常规线");
                player.Notify("replyEnterStory", new() { ["r"] = true });
                break;
            case "clientAskTriggerNewbee": Require(player.Newbee.Request(Int(args, "e")), "引导前置条件不满足"); break;
            case "clientFinishNewbee": Require(player.Newbee.Finish(Ints(args, "e")), "引导尚未开始"); break;
            case "passInitPrologue": Require(player.Newbee.PassPrologue(), "序章尚未完成"); break;
            case "checkProcessingEvent": player.Notify("triggerProcessingEvent", new() { ["pe"] = player.Newbee.Processing }); break;
            case "requestLuckyDrawFile":
                Require(player.Newbee.OpenSummon(Int(args, "t")), "仅支持剧情教学召唤");
                player.Notify("replyLuckyDrawFile", new() { ["rt"] = true, ["t"] = Int(args, "t") });
                break;
            case "requestLuckyDrawCard":
                var reward = player.Newbee.DrawSummon(Int(args, "t"), Int(args, "idx")) ?? throw new InvalidOperationException("教学召唤条件不满足");
                player.Notify("replyLuckyDrawCard", new() { ["rt"] = true, ["t"] = Int(args, "t"), ["info"] = reward, ["prog"] = new Dictionary<string, object>() });
                break;
            case "requestFile":
                player.Notify("replyFile", new() { ["rt"] = player.Newbee.CanSummon, ["info"] = player.Newbee.SummonFile(), ["t"] = Int(args, "t") });
                break;
            case "requestQuitFile":
                Require(player.Newbee.CloseSummon(Int(args, "t")));
                player.Notify("replyQuitFile", new() { ["rt"] = true, ["t"] = Int(args, "t") });
                break;
        }
    }
}
