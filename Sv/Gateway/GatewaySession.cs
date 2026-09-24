using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using Google.Protobuf;
using ICSharpCode.SharpZipLib.Zip.Compression;
using MongoDB.Bson;
using Org.BouncyCastle.Crypto.Engines;
using Serilog;
using Sv.Configuration;
using Sv.Game;
using Sv.Gateway.Packets;
using Sv.Gateway.Protocol;
using Mobile.Server;

namespace Sv.Gateway;

public readonly record struct GatewayFrame(ushort Method, byte[] Payload);

/// <summary>
/// 网关客户端会话。
/// </summary>
public sealed class GatewaySession
{
    private readonly ILogger _logger;
    private readonly NetworkStream _stream;
    private readonly Lock _sendLock = new();
    private readonly Queue<GatewayFrame> _pendingFrames = new();
    private readonly byte[] _wireBuffer = new byte[8192];
    private readonly byte[] _inflateBuffer = new byte[8192];
    private readonly byte[] _header = new byte[4];
    private int _headerCount;
    private byte[]? _body;
    private int _bodyCount;
    private RC4Engine? _encryptor;
    private RC4Engine? _decryptor;
    private Deflater? _deflater;
    private Inflater? _inflater;
    private int _closed;

    public GatewaySession(long connectionId, NetworkStream stream, GatewayHostedService server)
    {
        ConnectionId = connectionId;
        _logger = Log.ForContext<GatewaySession>().ForContext("ConnectionId", connectionId);
        _stream = stream;
        Server = server;
        AccountEntityId = ByteString.CopyFrom(ObjectId.GenerateNewId().ToByteArray());
    }

    public long ConnectionId { get; }

    public GatewayHostedService Server { get; }

    public ByteString AccountEntityId { get; }

    public ByteString? AvatarEntityId { get; private set; }

    public Player? Player { get; private set; }

    public long PlayerUid => Player?.Uid ?? 0;

    public string Stage { get; private set; } = "seed";

    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public bool ReliableInitialized { get; internal set; }

    public void SetStage(string stage) => Stage = stage;

    public void BindPlayer(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (IsClosed)
        {
            throw new InvalidOperationException("不能为已关闭的 Gateway 会话绑定玩家");
        }

        Player = player;
        player.Session = this;
        AvatarEntityId = ByteString.CopyFrom(ObjectId.Parse(player.Profile.AvatarId).ToByteArray());
        Stage = "authenticated";
    }

    public void UnbindPlayer()
    {
        if (Player is { } player && ReferenceEquals(player.Session, this))
        {
            player.Session = null;
        }
        Player = null;
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        UnbindPlayer();
        try
        {
            _stream.Close();
        }
        catch
        {
        }
    }

    public void Kick()
    {
        Close();
    }

    public bool SendPack(BasePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (IsClosed)
        {
            return false;
        }

        IMessage message = packet.CreateMessage();
        byte[] payload = message.ToByteArray();
        if (payload.Length + 2 > Config.Server.GatewayMaxFrameBytes)
        {
            throw new InvalidDataException("发送帧过大");
        }

        string rpcName;
        string rpcHash;
        int length;

        if (message is EntityMessage em)
        {
            rpcHash = Convert.ToHexString(em.Method.Md5.Span);
            rpcName = GatewayRouter.GetRpcName(rpcHash);
            length = em.Parameters.Length;
        }
        else if (message is EntityInfo ei)
        {
            rpcHash = Convert.ToHexString(ei.Type.Md5.Span);
            rpcName = GatewayRouter.GetRpcName(rpcHash);
            length = ei.Info.Length;
        }
        else if (packet is SessionSeedReplyPacket)
        {
            rpcName = "seed_reply";
            rpcHash = GatewayRouter.GetRpcHash(rpcName);
            length = payload.Length;
        }
        else if (packet is SessionKeyOkPacket)
        {
            rpcName = "session_key_ok";
            rpcHash = GatewayRouter.GetRpcHash(rpcName);
            length = payload.Length;
        }
        else if (packet is ConnectServerReplyPacket)
        {
            rpcName = "connect_server_reply";
            rpcHash = GatewayRouter.GetRpcHash(rpcName);
            length = payload.Length;
        }
        else
        {
            rpcName = packet.GetType().Name;
            rpcHash = GatewayRouter.GetRpcHash(rpcName);
            length = payload.Length;
        }

        if (string.IsNullOrEmpty(rpcName))
        {
            rpcName = "Unknown";
        }

        _logger.Information("发送 Gateway RPC，连接 {ConnectionId}，RPC Name {CommandID}，RPC Hash {CommandName} ，参数长度 {Length}，状态 {State}",
            ConnectionId, rpcName, rpcHash, length, Stage);

        byte[] frame = new byte[payload.Length + 6];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)(payload.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), packet.Method);
        payload.CopyTo(frame, 6);

        lock (_sendLock)
        {
            if (IsClosed)
            {
                return false;
            }

            try
            {
                if (_deflater is not null)
                {
                    using MemoryStream compressed = new();
                    _deflater.SetInput(frame);
                    _deflater.Flush();
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = _deflater.Deflate(buffer)) > 0)
                    {
                        compressed.Write(buffer, 0, count);
                    }
                    frame = compressed.ToArray();
                }

                _encryptor?.ProcessBytes(frame, 0, frame.Length, frame, 0);
                _stream.Write(frame);
                _stream.Flush();
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }
    }

    public async Task RunAsync()
    {
        try
        {
            await HandshakeAsync();
            while (!IsClosed)
            {
                GatewayFrame frame = await ReadFrameAsync();
                if (frame.Method == 4)
                {
                    Md5OrIndex registration = Md5OrIndex.Parser.ParseFrom(frame.Payload);
                    if (registration.Md5.Length != 16 || registration.Index <= 0)
                    {
                        throw new InvalidDataException("实体方法索引注册无效");
                    }

                    string regHash = Convert.ToHexString(registration.Md5.Span);
                    string regName = GatewayRouter.GetRpcName(regHash);
                    if (regName == "Unknown") regName = "register_method";
                    _logger.Debug("Gateway 方法索引注册，连接 {ConnectionId} method={Method} hash={Hash} index={Index}",
                        ConnectionId, regName, regHash, registration.Index);
                    continue;
                }

                if (frame.Method != 3)
                {
                    throw new InvalidDataException("当前状态不允许该 RPC");
                }

                EntityMessage message = EntityMessage.Parser.ParseFrom(frame.Payload);
                GatewayRouter.Instance.Route(this, message);
            }
        }
        finally
        {
            if (Player is not null)
            {
                Server.RemoveSession(Player.Uid);
            }
            Close();
        }
    }

    private async Task HandshakeAsync()
    {
        // 首包 seed_request
        GatewayFrame frame = await ReadExactFrameAsync();
        if (frame.Method != 0 || frame.Payload.Length != 0)
        {
            throw new InvalidDataException("首包必须是 seed_request");
        }

        string seedReqHash = GatewayRouter.GetRpcHash("seed_request");
        _logger.Information("收到 Gateway RPC，连接 {ConnectionId}，RPC Name {CommandID}，RPC Hash {CommandName} ，参数长度 {Length}，状态 {State}",
            ConnectionId, "seed_request", seedReqHash, 0, Stage);

        long seed = BinaryPrimitives.ReadInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) & long.MaxValue;
        SendPack(new SessionSeedReplyPacket(seed));
        Stage = "session_key";

        // session_key
        frame = await ReadExactFrameAsync();
        if (frame.Method != 1)
        {
            throw new InvalidDataException("未收到 session_key");
        }

        string sessionKeyHash = GatewayRouter.GetRpcHash("session_key");
        _logger.Information("收到 Gateway RPC，连接 {ConnectionId}，RPC Name {CommandID}，RPC Hash {CommandName} ，参数长度 {Length}，状态 {State}",
            ConnectionId, "session_key", sessionKeyHash, frame.Payload.Length, Stage);

        EncryptString encrypted = EncryptString.Parser.ParseFrom(frame.Payload);
        byte[] decrypted = AeadTool.DecryptRsaOaep(encrypted.Encryptstr.ToByteArray());
        try
        {
            SessionKey sessionKey = SessionKey.Parser.ParseFrom(decrypted);
            if (!sessionKey.HasSeed || sessionKey.Seed != seed || sessionKey.SessionKey_.Length != 20 ||
                sessionKey.RandomPaddingHeader.Length is < 2 or > 9 || sessionKey.RandomPaddingTail.Length is < 2 or > 9)
            {
                throw new InvalidDataException("会话密钥或 seed 校验失败");
            }

            byte[] key = sessionKey.SessionKey_.ToByteArray();
            try
            {
                _encryptor = AeadTool.CreateRc4Engine(key, true);
                _decryptor = AeadTool.CreateRc4Engine(key, false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
        }

        SendPack(new SessionKeyOkPacket());
        Stage = "connect_server";

        // connect_server
        frame = await ReadExactFrameAsync();
        if (frame.Method != 2)
        {
            throw new InvalidDataException("未收到 connect_server");
        }

        string connReqHash = GatewayRouter.GetRpcHash("connect_server");
        _logger.Information("收到 Gateway RPC，连接 {ConnectionId}，RPC Name {CommandID}，RPC Hash {CommandName} ，参数长度 {Length}，状态 {State}",
            ConnectionId, "connect_server", connReqHash, frame.Payload.Length, Stage);

        ConnectServerRequest request = ConnectServerRequest.Parser.ParseFrom(frame.Payload);
        if (!request.HasType || request.Deviceid.Length > 256)
        {
            throw new InvalidDataException("连接请求无效");
        }

        _deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, noZlibHeaderOrFooter: false);
        _inflater = new Inflater(noHeader: false);

        if (request.Type != ConnectServerRequest.Types.RequestType.NewConnection)
        {
            SendPack(new ConnectServerReplyPacket(ConnectServerReply.Types.ReplyType.ReconnectFailed));
            throw new InvalidDataException("尚不支持恢复旧会话，需重新登录");
        }

        SendPack(new ConnectServerReplyPacket(ConnectServerReply.Types.ReplyType.Connected));
        SendPack(new ClientAccountPacket(AccountEntityId));
        Stage = "account_login";
        _logger.Information("Gateway 连接 {ConnectionId} 握手完成", ConnectionId);
    }

    private async Task<GatewayFrame> ReadExactFrameAsync()
    {
        byte[] prefix = new byte[4];
        await _stream.ReadExactlyAsync(prefix);
        _decryptor?.ProcessBytes(prefix, 0, prefix.Length, prefix, 0);

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length < 2 || length > Config.Server.GatewayMaxFrameBytes)
        {
            throw new InvalidDataException("RPC 帧长度越界");
        }

        byte[] packet = new byte[length];
        await _stream.ReadExactlyAsync(packet);
        _decryptor?.ProcessBytes(packet, 0, packet.Length, packet, 0);

        return new GatewayFrame(BinaryPrimitives.ReadUInt16LittleEndian(packet), packet[2..]);
    }

    private async Task<GatewayFrame> ReadFrameAsync()
    {
        if (_inflater is null)
        {
            return await ReadExactFrameAsync();
        }

        while (_pendingFrames.Count == 0)
        {
            int read = await _stream.ReadAsync(_wireBuffer);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            _decryptor?.ProcessBytes(_wireBuffer, 0, read, _wireBuffer, 0);
            _inflater.SetInput(_wireBuffer, 0, read);
            int expanded = 0;
            while (true)
            {
                int count = _inflater.Inflate(_inflateBuffer);
                expanded += count;
                if (expanded > Config.Server.GatewayMaxFrameBytes * 4)
                {
                    throw new InvalidDataException("压缩流展开量超过限制");
                }

                Feed(_inflateBuffer.AsSpan(0, count));
                if (_inflater.IsFinished || _inflater.IsNeedingDictionary)
                {
                    throw new InvalidDataException("不支持结束或带外字典的压缩流");
                }

                if (count == 0)
                {
                    if (_inflater.IsNeedingInput) break;
                    throw new InvalidDataException("压缩流无法继续解码");
                }
            }
        }

        return _pendingFrames.Dequeue();
    }

    private void Feed(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (_body is null)
            {
                int count = Math.Min(4 - _headerCount, bytes.Length);
                bytes[..count].CopyTo(_header.AsSpan(_headerCount));
                _headerCount += count;
                bytes = bytes[count..];
                if (_headerCount < 4) continue;

                uint length = BinaryPrimitives.ReadUInt32LittleEndian(_header);
                if (length < 2 || length > Config.Server.GatewayMaxFrameBytes)
                {
                    throw new InvalidDataException("RPC 帧长度越界");
                }

                _body = new byte[length];
                _headerCount = 0;
                _bodyCount = 0;
            }

            int consumed = Math.Min(_body.Length - _bodyCount, bytes.Length);
            bytes[..consumed].CopyTo(_body.AsSpan(_bodyCount));
            _bodyCount += consumed;
            bytes = bytes[consumed..];
            if (_bodyCount == _body.Length)
            {
                if (_pendingFrames.Count >= 256)
                {
                    throw new InvalidDataException("待处理 RPC 数量超过限制");
                }

                _pendingFrames.Enqueue(new GatewayFrame(
                    BinaryPrimitives.ReadUInt16LittleEndian(_body), _body[2..]));
                _body = null;
            }
        }
    }
}
