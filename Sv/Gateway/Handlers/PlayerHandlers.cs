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
            Logger.Information("Gateway {ConnectionId} 收到角色心跳，基础登录已完成 UID={UserId}", session.ConnectionId, session.Player?.Uid);
        session.SetStage("online");
    }

    public void OnSyncServerTime(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is null) return;
        session.SendPack(new SyncServerTimePacket(session.AvatarEntityId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public void OnIncHeroStarOrder(GatewaySession session, HeroStarOrderRequestPacket? request)
    {
        if (session.AvatarEntityId is null || session.Player is null) return;
        lock (session.Player.SyncRoot)
        {
            bool openedArtifact = false;
            bool upgraded = request is not null && session.Player.HeroMgr.IncStarOrder(request.HeroId, out openedArtifact);
            session.Player.Save();
            // 失败回 h=-1;成功时回真实 heroId,并带上 artifact 告诉客户端这是升星还是开启神器。
            // 客户端在 artifact=false 时会自行 incStarOrder(),所以不能同时下发已自增的快照。
            session.SendPack(new HeroStarOrderReplyPacket(session.AvatarEntityId, upgraded ? request!.HeroId : -1, upgraded && openedArtifact));
        }
    }

    public void OnTeamOnLoginAsk(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is not null) session.SendPack(new TeamOnLoginReplyPacket(session.AvatarEntityId));
    }

    public void OnLimitTeamOnLoginAsk(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is not null) session.SendPack(new LimitTeamOnLoginReplyPacket(session.AvatarEntityId));
    }

    public void OnMultiTeamOnLoginAsk(GatewaySession session, BsonDocument args)
    {
        if (session.AvatarEntityId is not null) session.SendPack(new MultiTeamOnLoginReplyPacket(session.AvatarEntityId));
    }

    public void OnLogout(GatewaySession session, BsonDocument args)
    {
        Logger.Information("Gateway {ConnectionId} 客户端请求登出 UID={UserId}", session.ConnectionId, session.Player?.Uid);
        session.Close();
    }
}
