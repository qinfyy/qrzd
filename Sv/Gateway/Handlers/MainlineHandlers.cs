using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Serilog;
using Sv.Configuration;
using Sv.Game;
using Sv.Gateway.Packets;

namespace Sv.Gateway.Handlers;

public sealed class MainlineHandlers
{
    private static readonly ILogger Logger = Log.ForContext<MainlineHandlers>();

    public void OnRequest(GatewaySession session, string method, BsonDocument args, int sequence = 0, int callback = 0)
    {
        if (session.Player is not { } player || session.AvatarEntityId is not { } entity) return;
        callback = callback != 0 ? callback : args.GetValue("_cbid_", 0).AsInt32;
        lock (player.SyncRoot)
        {
            if (sequence > 0 && !session.ReliableInitialized)
            {
                player.Rpc.BeginConnection(sequence);
                session.ReliableInitialized = true;
            }
            BsonDocument parameters = args.DeepClone().AsBsonDocument;
            parameters.Remove("_cbid_");
            byte[] request = parameters.ToBson();
            if (sequence > 0 && player.Rpc.Find(sequence) is { } receipt)
            {
                if (receipt.Method != method || !receipt.Request.Span.SequenceEqual(request))
                {
                    Logger.Warning("City UID={Uid} 可靠序号内容冲突 seq={Sequence} method={Method}", player.Uid, sequence, method);
                    session.Close();
                    return;
                }
                session.SendPack(new ReliableRpcAckPacket(entity, sequence));
                foreach (var reply in receipt.Replies) Send(reply.Method, BsonSerializer.Deserialize<BsonDocument>(reply.Parameters.ToByteArray()));
                return;
            }
            if (sequence > player.Rpc.MaxSequence + 1)
            {
                session.SendPack(new AvatarRpcPacket(entity, "askReliableRpcSeq", new BsonDocument("s", player.Rpc.MaxSequence + 1)));
                return;
            }
            if (sequence > 0 && sequence <= player.Rpc.MaxSequence)
            {
                session.SendPack(new ReliableRpcAckPacket(entity, sequence));
                Send("serverCallbackCarrier", new BsonDocument("_r_", new BsonDocument { ["_result_"] = false, ["r"] = false, ["error"] = "expired_sequence" }));
                return;
            }

            byte[] before = player.SaveToBlob();
            string status = player.Status.CurrentStatus;
            List<(string Method, BsonDocument Args)> replies = [];
            void Collect(string name, Dictionary<string, object> values) => replies.Add((name, BsonHelper.ToBsonVal(values).AsBsonDocument));
            player.Notification += Collect;
            bool success = true;
            string error = "";
            try
            {
                Dispatch(player, method, parameters);
                if (method is not ("pullEvents" or "reliableEcho" or "queryEventOptAsk" or "getAllAreaInfoRequest" or "getAreaInfoRequest" or "getAreaStagesRequest"))
                    player.EventTrigger.Refresh();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or InvalidCastException or OverflowException or NotSupportedException
                or KeyNotFoundException or System.Text.Json.JsonException or InvalidDataException)
            {
                player.Restore(before);
                replies.Clear();
                success = false;
                error = ex.Message;
                Failure(player, method, parameters, error);
                Logger.Warning("City UID={Uid} 请求失败 seq={Sequence} method={Method} error={Error}", player.Uid, sequence, method, error);
            }
            catch
            {
                player.Restore(before);
                throw;
            }
            finally { player.Notification -= Collect; }

            // The callback belongs to the outer reliable call, not to its acknowledgement or business reply.
            replies.Add(("serverCallbackCarrier", new BsonDocument("_r_", new BsonDocument { ["_result_"] = success, ["r"] = success, ["error"] = error })));
            try
            {
                if (sequence > 0) player.Rpc.Remember(sequence, method, request, replies.Select(reply => (reply.Method, reply.Args.ToBson())));
                player.Save();
            }
            catch
            {
                player.Restore(before);
                throw;
            }
            Logger.Information("City UID={Uid} RPC seq={Sequence} cb={Callback} method={Method} ok={Success} status={Before}->{After} week={Week} day={Day}",
                player.Uid, sequence, callback, method, success, status, player.Status.CurrentStatus, player.WeekNum.Week, player.WeekNum.Day);
            if (sequence > 0) session.SendPack(new ReliableRpcAckPacket(entity, sequence));
            foreach (var reply in replies) Send(reply.Method, reply.Args);
        }

        void Send(string name, BsonDocument values)
        {
            if (name == "serverCallbackCarrier" && callback == 0) return;
            if (name == "serverCallbackCarrier" && callback != 0) values["_cbid_"] = callback;
            session.SendPack(new AvatarRpcPacket(entity, name, values));
        }
    }

    private static void Dispatch(Player player, string method, BsonDocument args)
    {
        if (method == "updateClientAvatarAsk") player.Notify("updateClientAvatarReply", new() { ["s"] = player.ToAvatarSnapshot(Config.Server) });
        else if (StoryHandlers.Methods.Contains(method)) StoryHandlers.OnRequest(player, method, args);
        else if (CityHandlers.Methods.Contains(method)) CityHandlers.OnRequest(player, method, args);
        else if (CombatHandlers.Methods.Contains(method)) CombatHandlers.OnRequest(player, method, args);
        else if (SocialHandlers.Methods.Contains(method)) SocialHandlers.OnRequest(player, method, args);
        else if (method is "clientLog" or "clientError" or "reportClientError" or "reportClientException" or "exceptUpload")
            Logger.Warning("City UID={Uid} 客户端报告 {Method}: {Arguments}", player.Uid, method, args.ToJson());
        else if (method is "uploadDeviceInfo" or "uploadTouchHistory" or "logCheckCheat" or "SALog" or "sendSALog")
            Logger.Debug("City UID={Uid} 客户端遥测 {Method}: {Arguments}", player.Uid, method, args.ToJson());
        else if (method is not ("reliableRpcAck" or "askReliableRpcSeq")) throw new NotSupportedException($"首周暂未支持入口: {method}");
    }

    private static void Failure(Player player, string method, BsonDocument args, string error)
    {
        int Number(string key) => args.TryGetValue(key, out var value) && value.IsNumeric ? value.ToInt32() : 0;
        if (method is "statusSwitch" or "statusCheck")
            player.Notify(method + "Reply", new() { ["r"] = "failed", ["s"] = args.GetValue("s", "").ToString()! });
        if (method is "startEvent" or "startEventOption") player.Notify("startEventReply", new() { ["e"] = Number("e"), ["r"] = 0 });
        if (method == "combatOfflineRequest") player.Notify("combatOfflineReply", new()
        {
            ["t"] = 0, ["s"] = Number("s"), ["f"] = "", ["h"] = Array.Empty<int>(), ["d"] = new Dictionary<string, object>(), ["c"] = new Dictionary<string, object>(),
        });
        if (method == "combatOfflineFinish")
        {
            player.Combat.ResetBattle();
            player.Notify("combatOfflineReward", new()
            {
                ["t"] = Number("t"), ["s"] = Number("s"), ["h"] = Array.Empty<int>(), ["x"] = Array.Empty<int>(),
                ["r"] = new Dictionary<string, object>(), ["w"] = 0,
            });
        }
        if (method is "areaBuildRequest" or "areaLevelUpRequest") player.Notify(method == "areaBuildRequest" ? "area_build_reply" : "area_level_up_reply",
            new() { ["rs"] = false, ["aid"] = Number("aid"), ["ext"] = new Dictionary<string, object>() });
        if (method == "enterPatrolRequest") player.Notify("enter_patrol_reply", new()
        {
            ["s"] = false, ["rs"] = false, ["ext"] = new Dictionary<string, object>(), ["ev"] = Array.Empty<int>(),
        });
        if (method == "zhaiRequest") player.Notify("zhaiReply", new() { ["rs"] = false, ["reward"] = new Dictionary<string, object>() });
        if (method == "incDayWhenNoActionVal") player.Notify("updateDay", new() { ["d"] = player.WeekNum.Day });
        if (method is "requestEnterStory" or "requestEnterStoryByEnding")
            player.Notify(method == "requestEnterStory" ? "replyEnterStory" : "replyEnterStoryByEnding", new() { ["r"] = false });
        if (method is "requestLuckyDrawFile" or "requestQuitFile")
            player.Notify(method == "requestLuckyDrawFile" ? "replyLuckyDrawFile" : "replyQuitFile", new() { ["rt"] = false, ["t"] = Number("t") });
        if (method == "requestLuckyDrawCard") player.Notify("replyLuckyDrawCard", new()
        {
            ["rt"] = false, ["t"] = Number("t"), ["info"] = new Dictionary<string, object>(), ["prog"] = new Dictionary<string, object>(),
        });
        player.Notify("showMessageStr", new() { ["m"] = error });
    }

    internal static int Int(BsonDocument args, string key, int fallback = 0) => args.GetValue(key, fallback).ToInt32();
    internal static int[] Ints(BsonDocument args, string key) => args.GetValue(key, new BsonArray()).AsBsonArray.Select(value => value.ToInt32()).ToArray();
    internal static void Require(bool condition, string message = "当前条件不满足")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
