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
                var entry = player.Combat.Enter(Int(args, "t"), Int(args, "s"), Ints(args, "h"), Int(data, "a"));
                player.Notify("combatOfflineReply", entry ?? throw new InvalidOperationException("战斗入场条件不满足"));
                break;
            case "combatOfflineFinish":
                var reward = player.Combat.Finish(Int(args, "t"), Int(args, "s"), Ints(args, "h"), Ints(args, "x"), Int(args, "w", -1));
                player.Notify("combatOfflineReward", reward ?? throw new InvalidOperationException("战斗结算与存档实例不匹配"));
                break;
            case "combatOfflineReset": player.Combat.ResetBattle(); break;
            case "combatOfflineHeroSwitch":
                var hero = player.Combat.SwitchHero(Int(args, "t"), Int(args, "s"), Int(args, "h"));
                Require(hero is not null, "换人条件不满足");
                player.Notify("combatOfflineHeroConfirm", new() { ["h"] = Int(args, "h"), ["d"] = hero! });
                break;
            case "combatOfflineHeroConfirm": Require(player.Combat.ConfirmHero(Int(args, "t"), Int(args, "s"), Int(args, "h"))); break;
        }
    }
}
