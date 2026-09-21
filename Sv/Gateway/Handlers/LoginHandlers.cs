using MongoDB.Bson;
using Serilog;
using Sv.Database;
using Sv.Game;
using Sv.Gateway.Packets;

namespace Sv.Gateway.Handlers;

public sealed class LoginHandlers
{
    private static readonly ILogger Logger = Log.ForContext<LoginHandlers>();

    public void OnLogin(GatewaySession session, LoginRequestPacket request)
    {
        if (request.ServerId != session.Options.ServerId)
        {
            session.SendPack(new LoginFailPacket(session.AccountEntityId, "区服参数不正确"));
            return;
        }

        try
        {
            Player player = GameDatabase.Instance.GetOrCreateByName(request.Name, request.ServerId);

            session.Server.BindPlayer(session, player);

            lock (player.SyncRoot)
            {
                player.OnLogin();
                player.Save();

                session.SendPack(new ClientAvatarPacket(session.AvatarEntityId!, player, session.Options));
                session.SendPack(new BecomePlayerPacket(session.AvatarEntityId!));
            }

            session.SetStage("avatar_sent");
            Logger.Information("Gateway 连接 {ConnectionId} 账号 UID={UserId} 登录", session.ConnectionId, player.Uid);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Gateway 连接 {ConnectionId} 账号登录处理异常", session.ConnectionId);
            session.SendPack(new LoginFailPacket(session.AccountEntityId, "服务端内部错误"));
        }
    }

    public void OnLoginWithUrs(GatewaySession session, BsonDocument args)
    {
        session.SendPack(new LoginFailPacket(session.AccountEntityId, "本地 Gateway 只支持调试账号登录"));
    }
}
