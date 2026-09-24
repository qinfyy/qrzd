using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Serilog;
using Sv.Gateway.Handlers;
using Sv.Gateway.Packets;
using Sv.Gateway.Protocol;
using Mobile.Server;

namespace Sv.Gateway;

/// <summary>
/// Gateway 协议路由器。
/// 持有全量编译期硬编码的 RPC 方法名与 MD5 散列双向索引，负责将实体报文快速路由至对应的业务处理器。
/// </summary>
public sealed class GatewayRouter
{
    public static GatewayRouter Instance { get; } = new();

    private static readonly ILogger Logger = Log.ForContext<GatewayRouter>();

    private static readonly FrozenDictionary<string, string> HashToName;
    private static readonly FrozenDictionary<string, string> NameToHash;

    static GatewayRouter()
    {
        Dictionary<string, string> hashToName = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> nameToHash = new(StringComparer.Ordinal);

        foreach (string name in RpcNames.All)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            string trimmed = name.Trim();
            string hash = AeadTool.HashMethodNameHex(trimmed);
            nameToHash.TryAdd(trimmed, hash);
            hashToName.TryAdd(hash, trimmed);
        }

        HashToName = hashToName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        NameToHash = nameToHash.ToFrozenDictionary(StringComparer.Ordinal);

        Logger.Information("Gateway RPC 路由表初始化完成，共索引 {RpcCount} 个 RPC 方法", HashToName.Count);
    }

    private readonly LoginHandlers _loginHandlers = new();
    private readonly PlayerHandlers _playerHandlers = new();
    private readonly MainlineHandlers _mainlineHandlers = new();

    public static int RpcCount => HashToName.Count;

    public static string GetRpcName(string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return "Unknown";
        }

        return HashToName.TryGetValue(hash, out string? name) ? name : "Unknown";
    }

    public static string GetMethodName(string digest) => GetRpcName(digest);

    public static string GetRpcHash(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return NameToHash.TryGetValue(name, out string? hash) ? hash : AeadTool.HashMethodNameHex(name);
    }

    public static bool TryGetRpcName(string hash, [NotNullWhen(true)] out string? name) => HashToName.TryGetValue(hash, out name);

    public static bool TryGetRpcHash(string name, [NotNullWhen(true)] out string? hash) => NameToHash.TryGetValue(name, out hash);

    public void Route(GatewaySession session, EntityMessage message)
    {
        if (!message.HasId || message.Method is null || message.Method.Index > 0 || message.Method.Md5.Length != 16 ||
            (message.Id != session.AccountEntityId && message.Id != session.AvatarEntityId))
        {
            throw new InvalidDataException("实体归属或方法标识无效");
        }

        string rpcHash = Convert.ToHexString(message.Method.Md5.Span);
        string rpcName = GetRpcName(rpcHash);
        int length = message.Parameters.Length;
        string state = session.Stage;

        Logger.Information("收到 Gateway RPC，连接 {ConnectionId}，RPC Name {CommandID}，RPC Hash {CommandName} ，参数长度 {Length}，状态 {State}",
            session.ConnectionId, rpcName, rpcHash, length, state);

        bool canRunBeforeLogin = rpcName is "login" or "loginWithUrs";
        if (!canRunBeforeLogin && session.Player is null)
        {
            Logger.Warning("未认证连接请求玩家方法，连接 {ConnectionId}，方法 {Method}", session.ConnectionId, rpcName);
            session.Close();
            return;
        }

        BsonDocument args = ReadArguments(message);

        switch (rpcName)
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
            case "pullEvents":
                _mainlineHandlers.OnRequest(session, rpcName, args);
                break;

            case "reliableRpcCall":
                ReliableRpcRequestPacket? rpc = ReliableRpcRequestPacket.FromBson(args);
                if (rpc is not null)
                {
                    _mainlineHandlers.OnRequest(session, rpc.SubMethod ?? "", rpc.Parameters ?? new BsonDocument(), rpc.RpcSeq, rpc.Cbid);
                }
                break;

            case "getAllAreaInfoRequest":
            case "getAreaInfoRequest":
            case "playerDestroyBuilding":
                _mainlineHandlers.OnRequest(session, rpcName, args);
                break;

            case "teamOnLoginAsk":
                _playerHandlers.OnTeamOnLoginAsk(session, args);
                break;

            case "limitTeamOnLoginAsk":
                _playerHandlers.OnLimitTeamOnLoginAsk(session, args);
                break;

            case "multiTeamOnLoginAsk":
                _playerHandlers.OnMultiTeamOnLoginAsk(session, args);
                break;

            case "enterPlace":
            case "tutorialBegin":
            case "updateStoryClickFlag":
                _mainlineHandlers.OnRequest(session, rpcName, args);
                break;

            case "add_chat_msg":
                _playerHandlers.OnAddChatMsg(session, args);
                break;

            case "incHeroStarOrder":
                _playerHandlers.OnIncHeroStarOrder(session, HeroStarOrderRequestPacket.FromBson(args));
                break;

            case "logout":
                _playerHandlers.OnLogout(session, args);
                break;

            default:
                _mainlineHandlers.OnRequest(session, rpcName, args);
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
