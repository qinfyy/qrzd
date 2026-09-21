using System.Buffers.Binary;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Serilog;
using Sv.Gateway.Handlers;
using Sv.Gateway.Packets;
using Sv.Gateway.Protocol;

namespace Sv.Gateway;

/// <summary>
/// 最SB的协议
/// </summary>
public sealed class GatewayRouter
{
    private static readonly ILogger Logger = Log.ForContext<GatewayRouter>();

    private static readonly string[] MethodNames =
    [
        "login", "loginWithUrs", "heartbeatServer", "syncServerTime",
        "uploadDeviceInfo", "logCheckCheat", "uploadTouchHistory", "uploadLocation",
        "reliableRpcCall", "pullEvents", "syncAllIntelligenceRequest", "logout",
    ];

    private static readonly Dictionary<string, string> Methods = MethodNames.ToDictionary(AeadTool.HashMethodNameHex, StringComparer.Ordinal);

    private readonly LoginHandlers _loginHandlers = new();
    private readonly PlayerHandlers _playerHandlers = new();

    public static string GetMethodName(string digest) =>
        Methods.TryGetValue(digest, out string? name) ? name : $"Unknown method ({digest})";

    public void Route(GatewaySession session, EntityMessage message)
    {
        if (!message.HasId || message.Method is null || message.Method.Index > 0 || message.Method.Md5.Length != 16 ||
            (message.Id != session.AccountEntityId && message.Id != session.AvatarEntityId))
        {
            throw new InvalidDataException("实体归属或方法标识无效");
        }

        string digest = Convert.ToHexString(message.Method.Md5.Span);
        string? method = Methods.GetValueOrDefault(digest);
        if (method is null)
        {
            Logger.Warning("未实现 Gateway 实体 RPC md5={MethodHash}，连接 {ConnectionId}，参数长度 {PayloadLength}",
                digest, session.ConnectionId, message.Parameters.Length);
            return;
        }

        bool canRunBeforeLogin = method is "login" or "loginWithUrs";
        if (!canRunBeforeLogin && session.Player is null)
        {
            Logger.Warning("未认证连接请求玩家方法，连接 {ConnectionId}，方法 {Method}",
                session.ConnectionId, method);
            session.Close();
            return;
        }

        BsonDocument args = ReadArguments(message);

        switch (method)
        {
            case "login":
                LoginRequestPacket? req = LoginRequestPacket.FromBson(args);
                if (req is null)
                {
                    session.SendPack(new LoginFailPacket(session.AccountEntityId, "登录参数无效"));
                    return;
                }
                _loginHandlers.OnLogin(session, req);
                break;

            case "loginWithUrs":
                _loginHandlers.OnLoginWithUrs(session, args);
                break;

            case "heartbeatServer":
                _playerHandlers.OnHeartbeat(session, args);
                break;

            case "syncServerTime":
                _playerHandlers.OnSyncServerTime(session, args);
                break;

            case "syncAllIntelligenceRequest":
                _playerHandlers.OnSyncAllIntelligence(session, args);
                break;

            case "pullEvents":
                _playerHandlers.OnPullEvents(session, PullEventsRequestPacket.FromBson(args));
                break;

            case "reliableRpcCall":
                ReliableRpcRequestPacket? rpc = ReliableRpcRequestPacket.FromBson(args);
                if (rpc is not null)
                {
                    _playerHandlers.OnReliableRpcCall(session, rpc);
                }
                break;

            case "logout":
                _playerHandlers.OnLogout(session, args);
                break;

            default:
                Logger.Debug("Gateway {ConnectionId} 暂未专门处理 {Method}", session.ConnectionId, method);
                break;
        }
    }

    private static BsonDocument ReadArguments(EntityMessage message)
    {
        if (message.Parameters.Length is < 5 or > 16384)
        {
            throw new InvalidDataException("BSON 参数长度无效");
        }
        byte[] bytes = message.Parameters.ToByteArray();
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != bytes.Length || bytes[^1] != 0)
        {
            throw new InvalidDataException("BSON 文档长度不匹配");
        }
        return BsonSerializer.Deserialize<BsonDocument>(bytes);
    }
}
