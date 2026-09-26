using MongoDB.Bson;
using Serilog;
using Sv.GameMaster;
using Sv.Gateway;
using Sv.Gateway.Packets;

namespace Sv.Game;

public sealed class ChatLogic(Player player) : PlayerLogicBase(player)
{
    public const string ServerFriendId = "000000000000000000000001";
    public const string ServerFriendName = "Server";
    public const long ServerFriendUid = 0;

    private readonly HashSet<(int Channel, int SubChannel)> _channels = [];
    private bool _greeted;

    protected internal override void OnLogin()
    {
        _channels.Clear();
        _greeted = false;
    }

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["hls"] = true,
        ["hlsf"] = false,
    };

    public Dictionary<string, object> ToFriendsSnapshot() => new()
    {
        ["fr"] = new object[] { new object[] { new Dictionary<string, string> { ["$oid"] = ServerFriendId }, ServerFriendData() } },
        // The built-in contact cannot receive friendship coins.
        ["fcgivei"] = new object[] { new object[] { new Dictionary<string, string> { ["$oid"] = ServerFriendId }, 0L } },
    };

    public void SetChannelSubscription(GatewaySession session, int channel, int subChannel, bool subscribe)
    {
        ValidateChannel(channel, subChannel);
        lock (Player.SyncRoot)
        {
            if (!subscribe)
            {
                _channels.Remove((channel, subChannel));
                return;
            }
            if (_channels.Count >= 64 && !_channels.Contains((channel, subChannel))) throw new InvalidDataException("聊天频道订阅过多");
            _channels.Add((channel, subChannel));
            SyncServerContact(session);
        }
    }

    public void SyncServerContact(GatewaySession session)
    {
        if (session.AvatarEntityId is not { } avatarId) return;
        lock (Player.SyncRoot)
        {
            SyncSocialAccess(session);
            session.SendPack(new AvatarRpcPacket(avatarId, "replyUpdateFriend", new BsonDocument
            {
                ["ft"] = 2,
                ["uuid"] = ObjectId.Parse(ServerFriendId),
                ["fd"] = BsonHelper.ToBsonVal(ServerFriendData()),
            }));
            if (_greeted) return;
            _greeted = true;
            EchoFromServer("你好，我是 Server。发送 help 查看 GM 命令，命令默认作用于自己。");
        }
    }

    public static void SyncSocialAccess(GatewaySession session)
    {
        if (session.AvatarEntityId is { } avatarId)
            session.SendPack(new AvatarRpcPacket(avatarId, "syncHighLevelSocialData", new BsonDocument { ["hls"] = true, ["hlsf"] = false }));
    }

    public BsonDocument GetPlayerInfo(GatewaySession session, IEnumerable<string> avatarIds)
    {
        BsonDocument result = [];
        foreach (string avatarId in avatarIds.Distinct())
        {
            if (avatarId == ServerFriendId)
            {
                result[avatarId] = new BsonDocument
                {
                    ["name"] = ServerFriendName, ["level"] = 1, ["userid"] = ServerFriendUid,
                    ["headImage"] = new BsonArray { 1, 1, "" }, ["bbsSkin"] = 0,
                };
                continue;
            }
            if (!ObjectId.TryParse(avatarId, out ObjectId targetId)) throw new InvalidDataException("玩家标识无效");
            GatewaySession? targetSession = FindSession(session, targetId);
            if (targetSession?.Player is not { } target) continue;
            lock (target.SyncRoot)
            {
                result[avatarId] = new BsonDocument
                {
                    ["name"] = target.Profile.NickName, ["level"] = target.Profile.Level, ["userid"] = target.Uid,
                    ["headImage"] = new BsonArray { 1, 1, "" }, ["bbsSkin"] = 0,
                };
            }
        }
        return result;
    }

    public void Receive(GatewaySession session, BsonDocument args)
    {
        if (!args.TryGetValue("cn", out BsonValue? channelValue) || !channelValue.IsInt32 ||
            !args.TryGetValue("scn", out BsonValue? subChannelValue) || !subChannelValue.IsInt32 ||
            !args.TryGetValue("ct", out BsonValue? textValue) || !textValue.IsString)
        {
            throw new InvalidDataException("聊天参数缺少 cn/scn/ct");
        }

        int channel = channelValue.AsInt32;
        int subChannel = subChannelValue.AsInt32;
        string text = textValue.AsString.Trim();
        ValidateChannel(channel, subChannel);
        if (text.Length is < 1 or > 500) throw new InvalidDataException("聊天内容须为 1 到 500 个字符");

        BsonDocument info = args.TryGetValue("info", out BsonValue? infoValue) && infoValue.IsBsonDocument ? infoValue.AsBsonDocument : new BsonDocument();
        ObjectId targetId = channel == 3 ? ReadAvatarId(info, "ti") : ObjectId.Empty;
        bool serverTalk = channel == 3 && targetId.ToString() == ServerFriendId;
        bool command = serverTalk || text.StartsWith('/') || text.StartsWith('!');

        BsonDocument message;
        lock (Player.SyncRoot)
        {
            message = CreateMessage(ObjectId.Parse(Player.Profile.AvatarId), Player.Profile.NickName, Player.Uid, Player.Profile.Level, text);
        }
        message["td"] = args.TryGetValue("td", out BsonValue? typeValue) && typeValue.IsBsonDocument ? typeValue.DeepClone() : new BsonDocument("t", 1);

        if (command)
        {
            // Commands and their replies belong to the Server conversation, never to another player or a public broadcast.
            SetRecipient(message, ObjectId.Parse(ServerFriendId), ServerFriendName, ServerFriendUid, 1);
            Send(session, 3, 1, message);
            GMTalk(session, text);
            return;
        }

        if (channel == 3)
        {
            GatewaySession? recipient = FindSession(session, targetId);
            if (recipient?.Player is not { } target) throw new InvalidDataException("对方不在线，暂不支持离线私聊");
            lock (target.SyncRoot)
            {
                SetRecipient(message, targetId, target.Profile.NickName, target.Uid, target.Profile.Level);
            }
            Send(session, channel, subChannel, message);
            if (!ReferenceEquals(recipient, session)) Send(recipient, channel, subChannel, message);
            return;
        }

        Send(session, channel, subChannel, message);
        if (channel is 1 or 9)
        {
            foreach (GatewaySession recipient in session.Server.OnlineSessions)
            {
                if (ReferenceEquals(recipient, session) || recipient.Player is not { } target) continue;
                bool subscribed;
                lock (target.SyncRoot) subscribed = target.Chat.IsSubscribed(channel, subChannel);
                if (subscribed) Send(recipient, channel, subChannel, message);
            }
        }
    }

    public void GMTalk(GatewaySession session, string content)
    {
        try
        {
            GameMasterService? gm = session.Server.GameMasterService;
            if (gm is null) EchoFromServer("GM 服务未启用");
            else
            {
                gm.Execute(content, Player);
                Log.Information("GMTalk UID={Uid} command={Command} 执行成功", Player.Uid, content);
            }
        }
        catch (Exception exception) when (exception is GameMasterCommandException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or OverflowException or InvalidDataException or FormatException)
        {
            EchoFromServer(exception.Message);
            Log.Warning("GMTalk UID={Uid} command={Command} error={Error}", Player.Uid, content, exception.Message);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "玩家 {Uid} GM 命令执行失败", Player.Uid);
            EchoFromServer("GM 命令执行失败，请查看服务端日志");
        }
    }

    public void EchoFromServer(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (Player.SyncRoot)
        {
            GatewaySession? session = Player.Session;
            if (session is null) return;
            for (int offset = 0; offset < text.Length;)
            {
                int length = Math.Min(500, text.Length - offset);
                if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
                BsonDocument message = CreateMessage(ObjectId.Parse(ServerFriendId), ServerFriendName, ServerFriendUid, 1, text.Substring(offset, length));
                SetRecipient(message, ObjectId.Parse(Player.Profile.AvatarId), Player.Profile.NickName, Player.Uid, Player.Profile.Level);
                Send(session, 3, 1, message);
                offset += length;
            }
        }
    }

    public static ObjectId ReadAvatarId(BsonDocument args, string key)
    {
        if (!args.TryGetValue(key, out BsonValue? value)) throw new InvalidDataException($"缺少玩家标识: {key}");
        if (value.IsObjectId) return value.AsObjectId;
        if (value.IsString && ObjectId.TryParse(value.AsString, out ObjectId id)) return id;
        throw new InvalidDataException("玩家标识无效");
    }

    private bool IsSubscribed(int channel, int subChannel) => _channels.Contains((channel, subChannel)) ||
        channel is 1 or 9 && (_channels.Contains((1, subChannel)) || _channels.Contains((9, subChannel)));

    private static void ValidateChannel(int channel, int subChannel)
    {
        if (channel is < 1 or > 18 or 15 || subChannel < 0) throw new InvalidDataException("聊天频道无效");
    }

    private static GatewaySession? FindSession(GatewaySession session, ObjectId targetId) => session.Server.OnlineSessions.FirstOrDefault(recipient =>
        !recipient.IsClosed && recipient.AvatarEntityId is { } id && id.Span.SequenceEqual(targetId.ToByteArray()));

    private static Dictionary<string, object> ServerFriendData() => new()
    {
        ["na"] = ServerFriendName, ["lv"] = 1, ["userid"] = ServerFriendUid, ["ol"] = true,
        ["headImage"] = new object[] { 1, 1, "" }, ["ca"] = false, ["ahi"] = 0,
        ["aff"] = new object[] { new object[] { "closer_fri_chat", false } },
    };

    private static void SetRecipient(BsonDocument message, ObjectId id, string name, long uid, int level)
    {
        message["ti"] = id;
        message["tn"] = name;
        message["tui"] = uid;
        message["tl"] = level;
        message["tai"] = "";
    }

    private static BsonDocument CreateMessage(ObjectId id, string name, long uid, int level, string text) => new()
    {
        ["si"] = id,
        ["sn"] = name,
        ["sui"] = uid,
        ["sl"] = level,
        ["sai"] = "",
        ["shi"] = new BsonArray { 1, 1, "" },
        ["ci"] = ObjectId.GenerateNewId(),
        ["tm"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["ct"] = text,
        ["td"] = new BsonDocument("t", 1),
    };

    private static void Send(GatewaySession session, int channel, int subChannel, BsonDocument message)
    {
        if (session.AvatarEntityId is { } avatarId) session.SendPack(new SyncChatMsgPacket(avatarId, channel, subChannel, message));
    }
}
