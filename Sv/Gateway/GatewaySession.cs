using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Serilog;
using Sv.Configuration;
using Sv.Gateway.Protocol;
using ProtoVoid = Sv.Gateway.Protocol.Void;

namespace Sv.Gateway;

public sealed class GatewaySession(
    long connectionId, GatewayConnection connection, GatewayKeys keys, LocalAccounts accounts,
    ServerOptions options)
{
    private static readonly string[] MethodNames =
    [
        "login", "loginWithUrs", "heartbeatServer", "syncServerTime",
        "uploadDeviceInfo", "logCheckCheat", "uploadTouchHistory", "uploadLocation",
        "reliableRpcCall", "pullEvents", "syncAllIntelligenceRequest", "logout",
    ];
    private static readonly Dictionary<string, string> Methods = MethodNames.ToDictionary(
        name => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(name))), StringComparer.Ordinal);
    private readonly ByteString accountEntityId = ByteString.CopyFrom(ObjectId.GenerateNewId().ToByteArray());
    private LocalAccount? account;
    private ByteString? avatarEntityId;
    public string Stage { get; private set; } = "seed";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await HandshakeAsync(cancellationToken);
            while (true)
            {
                GatewayFrame frame = await ReceiveAsync(cancellationToken);
                if (frame.Method == 4)
                {
                    Md5OrIndex registration = Md5OrIndex.Parser.ParseFrom(frame.Payload);
                    if (registration.Md5.Length != 16 || registration.Index <= 0)
                        throw new InvalidDataException("实体方法索引注册无效");
                    continue; // 可以继续发送 MD5，客户端索引是可选优化。
                }
                if (frame.Method != 3) throw new InvalidDataException("当前状态不允许该 RPC");
                await EntityMessageAsync(EntityMessage.Parser.ParseFrom(frame.Payload), cancellationToken);
            }
        }
        finally
        {
            if (account is not null) accounts.Release(account);
        }
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        GatewayFrame frame = await ReceiveAsync(cancellationToken);
        if (frame.Method != 0 || frame.Payload.Length != 0) throw new InvalidDataException("首包必须是 seed_request");
        long seed = BinaryPrimitives.ReadInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) & long.MaxValue;
        await connection.SendAsync(0, new SessionSeed { Seed = seed }, cancellationToken);
        Stage = "session_key";
        frame = await ReceiveAsync(cancellationToken);
        if (frame.Method != 1) throw new InvalidDataException("未收到 session_key");
        EncryptString encrypted = EncryptString.Parser.ParseFrom(frame.Payload);
        byte[] decrypted = keys.Decrypt(encrypted.Encryptstr.ToByteArray());
        try
        {
            SessionKey sessionKey = SessionKey.Parser.ParseFrom(decrypted);
            if (!sessionKey.HasSeed || sessionKey.Seed != seed || sessionKey.SessionKey_.Length != 20 ||
                sessionKey.RandomPaddingHeader.Length is < 2 or > 9 || sessionKey.RandomPaddingTail.Length is < 2 or > 9)
                throw new InvalidDataException("会话密钥或 seed 校验失败");
            byte[] key = sessionKey.SessionKey_.ToByteArray();
            try { connection.EnableEncryption(key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { CryptographicOperations.ZeroMemory(decrypted); }
        await connection.SendAsync(1, new ProtoVoid(), cancellationToken);
        Stage = "connect_server";
        frame = await ReceiveAsync(cancellationToken);
        if (frame.Method != 2) throw new InvalidDataException("未收到 connect_server");
        ConnectServerRequest request = ConnectServerRequest.Parser.ParseFrom(frame.Payload);
        if (!request.HasType || request.Deviceid.Length > 256) throw new InvalidDataException("连接请求无效");
        // 客户端在发送 connect_server 之后启用 zlib；回复及后续实体消息均走压缩流。
        connection.EnableCompression();
        if (request.Type != ConnectServerRequest.Types.RequestType.NewConnection)
        {
            await connection.SendAsync(2, new ConnectServerReply { Type = ConnectServerReply.Types.ReplyType.ReconnectFailed }, cancellationToken);
            throw new InvalidDataException("尚不支持恢复旧会话，需重新登录");
        }
        await connection.SendAsync(2, new ConnectServerReply { Type = ConnectServerReply.Types.ReplyType.Connected }, cancellationToken);
        await connection.SendAsync(3, new EntityInfo
        {
            Id = accountEntityId, Type = EncodeName("ClientAccount"), Info = ByteString.CopyFrom(new BsonDocument().ToBson()),
        }, cancellationToken);
        Stage = "account_login";
        Log.Information("Gateway {ConnectionId} RSA/ARC4/zlib 握手完成，已下发 ClientAccount", connectionId);
    }

    private async Task EntityMessageAsync(EntityMessage message, CancellationToken cancellationToken)
    {
        if (!message.HasId || message.Method is null || message.Method.Index > 0 || message.Method.Md5.Length != 16 ||
            (message.Id != accountEntityId && message.Id != avatarEntityId))
            throw new InvalidDataException("实体归属或方法标识无效");
        string digest = Convert.ToHexString(message.Method.Md5.Span);
        string? method = Methods.GetValueOrDefault(digest);
        if (method is null)
        {
            Log.Debug("Gateway {ConnectionId} 未实现实体 RPC md5={MethodHash} bytes={Length}", connectionId, digest, message.Parameters.Length);
            return;
        }
        if (method is "login" or "loginWithUrs")
        {
            if (message.Id != accountEntityId || account is not null)
                throw new InvalidDataException("登录阶段或实体错误");
            if (method == "loginWithUrs")
            {
                await LoginFailedAsync("本地 Gateway 只支持调试账号登录", cancellationToken);
                return;
            }
            BsonDocument args = ReadArguments(message);
            if (!args.TryGetValue("name", out BsonValue? name) || !name.IsString ||
                !args.TryGetValue("sv", out BsonValue? server) || !server.IsInt32 || server.AsInt32 != options.ServerId ||
                !args.TryGetValue("psw", out BsonValue? password) || !password.IsString || password.AsString.Length != 0)
            {
                await LoginFailedAsync("调试账号或区服参数不正确", cancellationToken);
                return;
            }
            account = accounts.Acquire(name.AsString, server.AsInt32);
            if (account is null)
            {
                await LoginFailedAsync("该本地账号已在线", cancellationToken);
                return;
            }
            avatarEntityId = ByteString.CopyFrom(ObjectId.Parse(account.AvatarId).ToByteArray());
            await connection.SendAsync(3, new EntityInfo
            {
                Id = avatarEntityId, Type = EncodeName("ClientAvatar"),
                Info = ByteString.CopyFrom(AvatarSnapshot.Create(account, options)),
            }, cancellationToken);
            await SendEntityAsync(avatarEntityId, "become_player", new BsonDocument(), cancellationToken);
            Stage = "avatar_sent";
            Log.Information("Gateway {ConnectionId} 本地账号 UID={UserId} 登录通过，已下发角色和 become_player；等待客户端心跳",
                connectionId, account.UserId);
            return;
        }
        if (account is null || message.Id != avatarEntityId) throw new InvalidDataException("角色尚未登录");
        if (method == "heartbeatServer")
        {
            _ = ReadArguments(message);
            await SendEntityAsync(avatarEntityId, "on_heartbeat", new BsonDocument(), cancellationToken);
            if (Stage != "online") Log.Information("Gateway {ConnectionId} 收到角色心跳，基础登录已完成 UID={UserId}", connectionId, account.UserId);
            Stage = "online";
        }
        else if (method == "syncServerTime")
        {
            await SendEntityAsync(avatarEntityId, "onSyncServerTime",
                new BsonDocument { ["t"] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, cancellationToken);
        }
        else if (method == "syncAllIntelligenceRequest")
        {
            await SendEntityAsync(avatarEntityId, "syncAllIntelligence",
                new BsonDocument
                {
                    ["is"] = new BsonDocument
                    {
                        ["is"] = new BsonArray(),
                        ["rd"] = false,
                    },
                }, cancellationToken);
        }
        else if (method == "pullEvents")
        {
            BsonDocument args = ReadArguments(message);
            int cbid = args.GetValue("_cbid_", 0).AsInt32;
            BsonDocument reply = new()
            {
                ["a"] = new BsonArray(),
                ["p"] = new BsonArray(),
            };
            if (cbid != 0) reply["_cbid_"] = cbid;
            await SendEntityAsync(avatarEntityId, "pullEventsReply", reply, cancellationToken);
        }
        else if (method == "reliableRpcCall")
        {
            BsonDocument args = ReadArguments(message);
            if (args.TryGetValue("w", out BsonValue? wVal) && wVal.IsBsonDocument)
            {
                BsonDocument wrapper = wVal.AsBsonDocument;
                string? subMethod = wrapper.GetValue("m", null)?.AsString;
                int rpcSeq = wrapper.GetValue("r", 0).AsInt32;
                int cbid = args.GetValue("_cbid_", 0).AsInt32;
                if (cbid == 0 && wrapper.TryGetValue("p", out BsonValue? pVal) && pVal.IsBsonDocument)
                {
                    cbid = pVal.AsBsonDocument.GetValue("_cbid_", 0).AsInt32;
                }

                await SendEntityAsync(avatarEntityId, "reliableRpcAck", new BsonDocument { ["s"] = rpcSeq }, cancellationToken);

                if (subMethod == "pullEvents")
                {
                    BsonDocument reply = new()
                    {
                        ["a"] = new BsonArray(),
                        ["p"] = new BsonArray(),
                    };
                    if (cbid != 0) reply["_cbid_"] = cbid;
                    await SendEntityAsync(avatarEntityId, "pullEventsReply", reply, cancellationToken);
                }
                else
                {
                    Log.Debug("Gateway {ConnectionId} reliableRpcCall 未专门处理子方法 {SubMethod}", connectionId, subMethod);
                }
            }
        }
        else
        {
            // 设备/进程/位置等上报不落盘，不把未知玩法请求当作成功。
            Log.Debug("Gateway {ConnectionId} 暂未处理 {Method}", connectionId, method);
        }
    }

    private static BsonDocument ReadArguments(EntityMessage message)
    {
        if (message.Parameters.Length is < 5 or > 16384) throw new InvalidDataException("BSON 参数长度无效");
        byte[] bytes = message.Parameters.ToByteArray();
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != bytes.Length || bytes[^1] != 0)
            throw new InvalidDataException("BSON 文档长度不匹配");
        return BsonSerializer.Deserialize<BsonDocument>(bytes);
    }

    private Task LoginFailedAsync(string reason, CancellationToken token) => SendEntityAsync(accountEntityId, "onLoginFail",
        new BsonDocument { ["e"] = 1, ["m"] = reason }, token);

    private Task SendEntityAsync(ByteString id, string method, BsonDocument arguments, CancellationToken token) =>
        connection.SendAsync(5, new EntityMessage { Id = id, Method = EncodeName(method), Parameters = ByteString.CopyFrom(arguments.ToBson()) }, token);

    private static Md5OrIndex EncodeName(string name) => new() { Md5 = ByteString.CopyFrom(MD5.HashData(Encoding.UTF8.GetBytes(name))) };

    private async Task<GatewayFrame> ReceiveAsync(CancellationToken token)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(account is null ? options.GatewayHandshakeSeconds : options.GatewayIdleSeconds));
        return await connection.ReadAsync(timeout.Token);
    }
}
