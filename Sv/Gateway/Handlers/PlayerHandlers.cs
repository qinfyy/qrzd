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

    public void OnReliableRpcCall(GatewaySession session, ReliableRpcRequestPacket request)
    {
        if (session.AvatarEntityId is null) return;

        session.SendPack(new ReliableRpcAckPacket(session.AvatarEntityId, request.RpcSeq));

        if (request.SubMethod == "pullEvents")
        {
            session.SendPack(new PullEventsReplyPacket(session.AvatarEntityId, request.Cbid));
        }
        else
        {
            Logger.Debug("Gateway {ConnectionId} reliableRpcCall 未专门处理子方法 {SubMethod}",
                session.ConnectionId, request.SubMethod);
        }
    }

    public void OnSyncAllIntelligence(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        bool readed = session.Player?.Intelligence.Readed ?? false;
        session.SendPack(new SyncAllIntelligencePacket(session.AvatarEntityId, readed));
    }

    public void OnLogout(GatewaySession session, BsonDocument args)
    {
        Logger.Information("Gateway {ConnectionId} 客户端请求登出 UID={UserId}",
            session.ConnectionId, session.Player?.Uid);
        session.Close();
    }
}
