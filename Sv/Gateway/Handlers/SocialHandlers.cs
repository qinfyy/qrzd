using MongoDB.Bson;
using Sv.Game;
using static Sv.Gateway.Handlers.MainlineHandlers;

namespace Sv.Gateway.Handlers;

public static class SocialHandlers
{
    public static readonly HashSet<string> Methods = ["readSocialMsg", "readPrivateMsg", "replyPrivateMsg", "triggerSocialMsgEvent", "syncAllIntelligenceRequest",
        "readTodayIntelligence", "handleIntelligenceRequest", "openIntelligenceRequest"];

    public static void OnRequest(Player player, string method, BsonDocument args)
    {
        switch (method)
        {
            case "readSocialMsg": Require(player.Social.Read(Int(args, "id"), false)); break;
            case "readPrivateMsg": Require(player.Social.Read(Int(args, "id"), true)); break;
            case "replyPrivateMsg": Require(player.Social.Reply(Int(args, "id"), Int(args, "bid")), "私信选项无效"); break;
            case "triggerSocialMsgEvent": Require(player.Social.Trigger(Int(args, "id")), "消息剧情条件不满足"); break;
            case "syncAllIntelligenceRequest": player.Intelligence.Synchronize(); break;
            case "readTodayIntelligence": player.Intelligence.Readed = true; break;
            case "handleIntelligenceRequest":
            case "openIntelligenceRequest":
                string uuid = args["uuid"].IsObjectId ? args["uuid"].AsObjectId.ToString() : args["uuid"].AsString;
                Require(method == "handleIntelligenceRequest" ? player.Intelligence.Handle(uuid) : player.Intelligence.Open(uuid), "情报不存在或情报值不足");
                player.Intelligence.Synchronize();
                player.City.Synchronize();
                player.Notify(method == "handleIntelligenceRequest" ? "handleIntelligenceSuccess" : "openIntelligenceSuccess",
                    new() { ["uuid"] = new Dictionary<string, string> { ["$oid"] = uuid } });
                break;
        }
    }
}
