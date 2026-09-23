using MongoDB.Bson;
using Serilog;
using Sv.GameMaster;
using Sv.Gateway;
using Sv.Gateway.Packets;

namespace Sv.Game;

public sealed class ChatLogic(Player player) : PlayerLogicBase(player)
{
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
        if (text.Length is < 1 or > 500 || channel is < 1 or > 18 || subChannel < 0)
        {
            throw new InvalidDataException("聊天频道或内容无效");
        }

        if (text.StartsWith('/') || text.Equals("@allhero", StringComparison.OrdinalIgnoreCase) || text.StartsWith("@allhero ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                GameMasterService? gm = session.Server.GameMasterService;
                if (gm is null) EchoFromServer("GM 服务未启用");
                else gm.Execute(text, Player);
            }
            catch (Exception exception) when (exception is GameMasterCommandException or ArgumentOutOfRangeException or
                KeyNotFoundException or OverflowException or InvalidDataException)
            {
                EchoFromServer(exception.Message);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "玩家 {Uid} GM 命令执行失败", Player.Uid);
                EchoFromServer("GM 命令执行失败");
            }
            return;
        }

        BsonDocument info = args.TryGetValue("info", out BsonValue? infoValue) && infoValue.IsBsonDocument ? infoValue.AsBsonDocument : new BsonDocument();
        BsonDocument message = CreateMessage(Player, text);
        message["td"] = args.TryGetValue("td", out BsonValue? typeValue) && typeValue.IsBsonDocument ? typeValue.AsBsonDocument : new BsonDocument();

        if (channel == 3)
        {
            if (!info.TryGetValue("ti", out BsonValue? toValue) ||
                !ObjectId.TryParse(toValue.IsObjectId ? toValue.AsObjectId.ToString() : toValue.ToString(), out ObjectId targetId))
            {
                EchoFromServer("私聊目标无效");
                return;
            }
            message["ti"] = targetId;
            message["tn"] = info.GetValue("tn", "");
            foreach (GatewaySession recipient in session.Server.OnlineSessions)
            {
                if (recipient.Player?.Profile.AvatarId == targetId.ToString()) Send(recipient, channel, subChannel, message);
            }
            Send(session, channel, subChannel, message);
            return;
        }

        if (channel is 1 or 9)
        {
            foreach (GatewaySession recipient in session.Server.OnlineSessions) Send(recipient, channel, subChannel, message);
        }
        else
        {
            Send(session, channel, subChannel, message);
        }
    }

    public void EchoFromServer(string text)
    {
        GatewaySession? session = Player.Session;
        if (session is null) return;
        BsonDocument message = CreateMessage(Player, text);
        message["sn"] = "GM";
        message["si"] = ObjectId.Empty;
        Send(session, 1, 1, message);
    }

    private static BsonDocument CreateMessage(Player player, string text) => new()
    {
        ["si"] = ObjectId.Parse(player.Profile.AvatarId),
        ["sn"] = player.Profile.NickName,
        ["sui"] = player.Uid,
        ["sl"] = player.Profile.Level,
        ["shi"] = new BsonArray(),
        ["ci"] = ObjectId.GenerateNewId(),
        ["tm"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["ct"] = text,
        ["td"] = new BsonDocument(),
    };

    private static void Send(GatewaySession session, int channel, int subChannel, BsonDocument message)
    {
        if (session.AvatarEntityId is { } avatarId) session.SendPack(new SyncChatMsgPacket(avatarId, channel, subChannel, message));
    }
}
