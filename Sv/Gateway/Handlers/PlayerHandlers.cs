using MongoDB.Bson;
using Serilog;
using Sv.Gateway.Packets;

namespace Sv.Gateway.Handlers;

public sealed class PlayerHandlers
{
    private static readonly ILogger Logger = Log.ForContext<PlayerHandlers>();

    public void OnHeartbeat(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        session.SendPack(new HeartbeatAckPacket(session.AvatarEntityId));
        if (session.Stage != "online")
        {
            Logger.Information("Gateway {ConnectionId} 收到角色心跳，基础登录已完成 UID={UserId}",
                session.ConnectionId, session.Player?.Uid);
        }
        session.SetStage("online");
    }

    public void OnSyncServerTime(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.SendPack(new SyncServerTimePacket(session.AvatarEntityId, now));
    }

    public void OnPullEvents(GatewaySession session, PullEventsRequestPacket request)
    {
        if (session.AvatarEntityId is null) return;
        session.SendPack(new PullEventsReplyPacket(session.AvatarEntityId, request.Cbid));
    }

    public void OnIncHeroStarOrder(GatewaySession session, HeroStarOrderRequestPacket? request)
    {
        if (session.AvatarEntityId is null || session.Player is null) return;

        lock (session.Player.SyncRoot)
        {
            bool upgraded = request is not null && session.Player.HeroMgr.IncStarOrder(request.HeroId);
            session.Player.Save();
            session.SendPack(new HeroStarOrderReplyPacket(session.AvatarEntityId, upgraded ? request!.HeroId : -1));
        }
    }

    public void OnReliableRpcCall(GatewaySession session, ReliableRpcRequestPacket request)
    {
        if (session.AvatarEntityId is null) return;

        session.SendPack(new ReliableRpcAckPacket(session.AvatarEntityId, request.RpcSeq));

        switch (request.SubMethod)
        {
            case "pullEvents":
                session.SendPack(new PullEventsReplyPacket(session.AvatarEntityId, request.Cbid));
                break;

            case "getAreaStagesRequest":
                if (request.Parameters is not null && session.Player is not null)
                {
                    int aid = request.Parameters.GetValue("aid", 0).AsInt32;
                    var areaSnap = session.Player.City.ToAreaSnapshot(aid);
                    if (areaSnap is not null && areaSnap.TryGetValue("stage", out object? stageObj) && stageObj is Dictionary<string, object> stageDict)
                    {
                        session.SendPack(new GetAreaStagesReplyPacket(session.AvatarEntityId, aid, stageDict));
                    }
                }
                break;

            case "areaBuildRequest":
                if (request.Parameters is not null && session.Player is not null)
                {
                    int aid = request.Parameters.GetValue("aid", 0).AsInt32;
                    int fi = request.Parameters.GetValue("fi", 0).AsInt32;
                    int bi = request.Parameters.GetValue("bi", 0).AsInt32;
                    int[] hs = request.Parameters.GetValue("hs", new BsonArray()).AsBsonArray.Select(v => v.AsInt32).ToArray();

                    lock (session.Player.SyncRoot)
                    {
                        bool success = session.Player.City.Build(aid, fi, bi, hs, out string? error);
                        if (success)
                        {
                            session.Player.Save();
                            var areaSnap = session.Player.City.ToAreaSnapshot(aid);
                            if (areaSnap is not null)
                            {
                                session.SendPack(new GetAreaInfoReplyPacket(session.AvatarEntityId, areaSnap));
                            }
                            session.SendPack(new GetCityDataReplyPacket(session.AvatarEntityId, session.Player.City.ToCityDataSnapshot()));
                            session.SendPack(new AreaBuildReplyPacket(session.AvatarEntityId, true, aid));
                        }
                        else
                        {
                            Logger.Warning("Gateway {ConnectionId} 建筑建造失败: {Error}", session.ConnectionId, error);
                            session.SendPack(new AreaBuildReplyPacket(session.AvatarEntityId, false, aid));
                        }
                    }
                }
                break;

            case "areaLevelUpRequest":
                if (request.Parameters is not null && session.Player is not null)
                {
                    int aid = request.Parameters.GetValue("aid", 0).AsInt32;
                    int luv = request.Parameters.GetValue("luv", 1).AsInt32;
                    int[] hs = request.Parameters.GetValue("hs", new BsonArray()).AsBsonArray.Select(v => v.AsInt32).ToArray();

                    lock (session.Player.SyncRoot)
                    {
                        bool success = session.Player.City.LevelUpArea(aid, luv, hs, out string? error);
                        if (success)
                        {
                            session.Player.Save();
                            var areaSnap = session.Player.City.ToAreaSnapshot(aid);
                            if (areaSnap is not null)
                            {
                                session.SendPack(new GetAreaInfoReplyPacket(session.AvatarEntityId, areaSnap));
                            }
                            session.SendPack(new GetCityDataReplyPacket(session.AvatarEntityId, session.Player.City.ToCityDataSnapshot()));
                            session.SendPack(new AreaLevelUpReplyPacket(session.AvatarEntityId, true, aid));
                        }
                        else
                        {
                            Logger.Warning("Gateway {ConnectionId} 区域升级失败: {Error}", session.ConnectionId, error);
                            session.SendPack(new AreaLevelUpReplyPacket(session.AvatarEntityId, false, aid));
                        }
                    }
                }
                break;

            case "enterPatrolRequest":
                if (request.Parameters is not null && session.Player is not null)
                {
                    int aid = request.Parameters.GetValue("aid", 0).AsInt32;
                    int[] hs = request.Parameters.GetValue("hs", new BsonArray()).AsBsonArray.Select(v => v.AsInt32).ToArray();

                    lock (session.Player.SyncRoot)
                    {
                        bool success = session.Player.City.Patrol(aid, hs, out string? error);
                        if (success)
                        {
                            session.Player.Save();
                            Dictionary<int, int> friendlyChanges = hs.ToDictionary(h => h, _ => 5);
                            session.SendPack(new NotifyAddFriendlyPacket(session.AvatarEntityId, friendlyChanges));
                            session.SendPack(new GetCityDataReplyPacket(session.AvatarEntityId, session.Player.City.ToCityDataSnapshot()));
                            session.SendPack(new EnterPatrolReplyPacket(session.AvatarEntityId, true, true));
                        }
                        else
                        {
                            Logger.Warning("Gateway {ConnectionId} 巡查失败: {Error}", session.ConnectionId, error);
                            session.SendPack(new EnterPatrolReplyPacket(session.AvatarEntityId, false, false));
                        }
                    }
                }
                break;

            case "zhaiRequest":
                if (session.Player is not null)
                {
                    lock (session.Player.SyncRoot)
                    {
                        bool success = session.Player.City.Zhai();
                        if (success)
                        {
                            session.Player.Save();
                            session.SendPack(new GetCityDataReplyPacket(session.AvatarEntityId, session.Player.City.ToCityDataSnapshot()));
                            session.SendPack(new ZhaiReplyPacket(session.AvatarEntityId, true, new Dictionary<string, object>
                            {
                                ["fatigue"] = 5,
                                ["money"] = 50,
                                ["exp"] = 10,
                            }));
                        }
                        else
                        {
                            session.SendPack(new ZhaiReplyPacket(session.AvatarEntityId, false));
                        }
                    }
                }
                break;

            default:
                Logger.Debug("Gateway {ConnectionId} reliableRpcCall 未专门处理子方法 {SubMethod}",
                    session.ConnectionId, request.SubMethod);
                break;
        }
    }

    public void OnGetAllAreaInfoRequest(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null || session.Player is null) return;
        session.SendPack(new GetAllAreaInfoReplyPacket(
            session.AvatarEntityId,
            session.Player.City.ToAreasSnapshot(),
            session.Player.City.ToCityDataSnapshot(),
            settlement: false));
    }

    public void OnGetAreaInfoRequest(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null || session.Player is null) return;
        int aid = args.GetValue("id", 0).AsInt32;
        var areaSnap = session.Player.City.ToAreaSnapshot(aid);
        if (areaSnap is not null)
        {
            session.SendPack(new GetAreaInfoReplyPacket(session.AvatarEntityId, areaSnap));
        }
    }

    public void OnPlayerDestroyBuilding(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null || session.Player is null) return;
        string? uuid = args.GetValue("u", null)?.AsString;
        if (!string.IsNullOrEmpty(uuid))
        {
            lock (session.Player.SyncRoot)
            {
                if (session.Player.City.DestroyBuilding(uuid, out int aid))
                {
                    session.Player.Save();
                    var areaSnap = session.Player.City.ToAreaSnapshot(aid);
                    if (areaSnap is not null)
                    {
                        session.SendPack(new GetAreaInfoReplyPacket(session.AvatarEntityId, areaSnap));
                    }
                    session.SendPack(new GetCityDataReplyPacket(session.AvatarEntityId, session.Player.City.ToCityDataSnapshot()));
                }
            }
        }
    }

    public void OnTeamOnLoginAsk(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        session.SendPack(new TeamOnLoginReplyPacket(session.AvatarEntityId));
    }

    public void OnLimitTeamOnLoginAsk(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        session.SendPack(new LimitTeamOnLoginReplyPacket(session.AvatarEntityId));
    }

    public void OnMultiTeamOnLoginAsk(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        session.SendPack(new MultiTeamOnLoginReplyPacket(session.AvatarEntityId));
    }

    public void OnEnterPlace(GatewaySession session, BsonDocument args)
    {
        int place = args.GetValue("p", 0).AsInt32;
        Logger.Debug("Gateway {ConnectionId} 进入场景/位置 {Place}", session.ConnectionId, place);
    }

    public void OnTutorialBegin(GatewaySession session, BsonDocument args)
    {
        Logger.Debug("Gateway {ConnectionId} 客户端开始新手引导", session.ConnectionId);
    }

    public void OnUpdateStoryClickFlag(GatewaySession session, BsonDocument args)
    {
        string? route = args.GetValue("r", null)?.AsString;
        Logger.Debug("Gateway {ConnectionId} 点击剧情分支 {Route}", session.ConnectionId, route);
    }

    public void OnSyncAllIntelligence(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        bool readed = session.Player?.Intelligence.Readed ?? false;
        session.SendPack(new SyncAllIntelligencePacket(session.AvatarEntityId, readed));
    }

    public void OnAddChatMsg(GatewaySession session, BsonDocument args)
    {
        session.Player?.Chat.Receive(session, args);
    }

    public void OnLogout(GatewaySession session, BsonDocument args)
    {
        Logger.Information("Gateway {ConnectionId} 客户端请求登出 UID={UserId}",
            session.ConnectionId, session.Player?.Uid);
        session.Close();
    }
}
