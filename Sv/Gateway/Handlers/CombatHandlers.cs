using MongoDB.Bson;
using Sv.Game;
using static Sv.Gateway.Handlers.MainlineHandlers;

namespace Sv.Gateway.Handlers;

public static class CombatHandlers
{
    public static readonly HashSet<string> Methods = ["combatOfflineRequest", "combatOfflineFinish", "combatOfflineReset", "combatOfflineHeroSwitch", "combatOfflineHeroConfirm"];

    public static void OnRequest(Player player, string method, BsonDocument args)
    {
        switch (method)
        {
            case "combatOfflineRequest":
                BsonDocument data = args.GetValue("d", new BsonDocument()).AsBsonDocument;
                if (player.Combat.Enter(Int(args, "t"), Int(args, "s"), Ints(args, "h"), Int(data, "a")) is { } entry)
                {
                    player.Notify("combatOfflineReply", entry);
                    break;
                }
                // 客户端 combatOfflineRequest 结尾无条件 gg.ui.lock()，只有 combatOfflineReply
                // 开头会 unlock。抛异常会 replies.Clear() 把这条回复清掉，UI 将永久锁死。
                // 因此这里下发失败回复（type=0 走 offlineFaild）并另行提示原因，不能抛。
                string reason = player.Combat.LastRejectReason ?? "条件不满足";
                player.Notify("combatOfflineReply", new Dictionary<string, object>
                {
                    ["t"] = 0, ["s"] = Int(args, "s"), ["f"] = "", ["h"] = Array.Empty<int>(),
                    ["d"] = new Dictionary<string, object>(), ["c"] = new Dictionary<string, object>(),
                });
                player.Notify("showMessageStr", new() { ["m"] = $"无法开始战斗：{reason}" });
                break;
            case "combatOfflineFinish":
                if (player.Combat.Finish(Int(args, "t"), Int(args, "s"), Ints(args, "h"), Ints(args, "x"), Int(args, "w", -1)) is { } reward)
                {
                    player.Notify("combatOfflineReward", reward);
                    break;
                }
                player.Notify("showMessageStr", new() { ["m"] = $"战斗结算失败：{player.Combat.LastRejectReason}" });
                break;
            case "combatOfflineReset": player.Combat.ResetBattle(); break;
            case "combatOfflineHeroSwitch":
                if (player.Combat.SwitchHero(Int(args, "t"), Int(args, "s"), Int(args, "h")) is { } hero)
                {
                    player.Notify("combatOfflineHeroConfirm", new() { ["h"] = Int(args, "h"), ["d"] = hero });
                    break;
                }
                player.Notify("showMessageStr", new() { ["m"] = $"换人失败：{player.Combat.LastRejectReason}" });
                break;
            case "combatOfflineHeroConfirm":
                if (!player.Combat.ConfirmHero(Int(args, "t"), Int(args, "s"), Int(args, "h")))
                    player.Notify("showMessageStr", new() { ["m"] = $"换人确认失败：{player.Combat.LastRejectReason}" });
                break;
        }
    }
}
