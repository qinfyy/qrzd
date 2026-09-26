using MongoDB.Bson;
using Serilog;
using Sv.Game;
using Sv.Gateway.Packets;

namespace Sv.Gateway.Handlers;

public sealed class ChatHandlers
{
    public void OnRequest(GatewaySession session, string method, BsonDocument args)
    {
        if (session.Player is not { } player || session.AvatarEntityId is not { } avatarId) return;
        bool success = false;
        string error = "";
        try
        {
            switch (method)
            {
                case "add_chat_msg":
                    player.Chat.Receive(session, args);
                    break;
                case "regist_chat_channel_listener":
                case "remove_chat_channel_listener":
                    player.Chat.SetChannelSubscription(session, args["cn"].AsInt32, args["scn"].AsInt32, method == "regist_chat_channel_listener");
                    break;
                case "queryFriendStatus":
                    player.Chat.SyncServerContact(session);
                    break;
                case "requestPlayerInfoFromClientLongList":
                    BsonArray ids = args["strids"].AsBsonArray;
                    if (ids.Count > 5) throw new InvalidDataException("Too many player IDs");
                    session.SendPack(new AvatarRpcPacket(avatarId, "replyPlayerInfoFromClientLongList", new BsonDocument
                    {
                        ["rs"] = player.Chat.GetPlayerInfo(session, ids.Select(value => value.AsString)),
                        ["t"] = args["t"].AsInt32,
                    }));
                    break;
                case "requestPlayerInfoKeyList":
                    session.SendPack(new AvatarRpcPacket(avatarId, "replyPlayerInfoKeyList", new BsonDocument
                    {
                        ["rs"] = player.Chat.GetPlayerInfo(session, [args["strid"].AsString]),
                    }));
                    break;
                case "readFriendCoin":
                    // Read markers live in the client's chat cache; the Server contact has no coin rewards.
                    _ = ChatLogic.ReadAvatarId(args, "tid");
                    break;
                case "requestSetBindPhoneState":
                    ChatLogic.SyncSocialAccess(session);
                    session.SendPack(new AvatarRpcPacket(avatarId, "replyRequestSetBindPhoneState", new BsonDocument
                    {
                        ["rs"] = true, ["sm"] = args.GetValue("sm", true).AsBoolean,
                    }));
                    break;
                default:
                    throw new NotSupportedException(method);
            }
            success = true;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidCastException or ArgumentException or FormatException or KeyNotFoundException)
        {
            error = exception.Message;
            player.Chat.EchoFromServer(error);
            Log.Warning("Chat UID={Uid} method={Method} error={Error}", player.Uid, method, error);
        }
        finally
        {
            if (method == "add_chat_msg") session.SendPack(new AvatarRpcPacket(avatarId, "removeInput", new BsonDocument("os", !success)));
            if (args.TryGetValue("_cbid_", out BsonValue? callback) && callback.IsInt32 && callback.AsInt32 != 0)
            {
                session.SendPack(new AvatarRpcPacket(avatarId, "serverCallbackCarrier", new BsonDocument
                {
                    ["_cbid_"] = callback,
                    ["_r_"] = new BsonDocument { ["_result_"] = success, ["r"] = success, ["error"] = error },
                }));
            }
        }
    }
}
