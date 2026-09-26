using MongoDB.Bson;
using Sv.Game;
using static Sv.Gateway.Handlers.MainlineHandlers;

namespace Sv.Gateway.Handlers;

public static class CityHandlers
{
    public static readonly HashSet<string> Methods = ["getAllAreaInfoRequest", "getAreaInfoRequest", "getAreaStagesRequest", "areaBuildRequest", "areaLevelUpRequest",
        "enterPatrolRequest", "patrolReward", "playerDestroyBuilding", "zhaiRequest", "canteenRequest", "incDayWhenNoActionVal", "clearCurDailySettlement"];

    public static void OnRequest(Player player, string method, BsonDocument args)
    {
        int area = Int(args, "aid");
        switch (method)
        {
            case "getAllAreaInfoRequest": player.City.Synchronize(); break;
            case "getAreaInfoRequest":
                player.Notify("get_area_info_reply", new() { ["ai"] = player.City.ToAreaSnapshot(Int(args, "id")) ?? throw new InvalidOperationException("区域不存在") });
                break;
            case "getAreaStagesRequest": player.Notify("get_area_stages_reply", new() { ["ai"] = area, ["st"] = player.City.ToStagesSnapshot(area) }); break;
            case "areaBuildRequest":
                Require(player.City.Build(area, Int(args, "fi"), Int(args, "bi"), Ints(args, "hs"), out string? buildError), buildError ?? "无法建造");
                break;
            case "areaLevelUpRequest":
                Require(player.City.LevelUpArea(area, Int(args, "luv", 1), Ints(args, "hs"), out string? developError), developError ?? "无法开发");
                break;
            case "enterPatrolRequest":
                Require(player.City.Patrol(area, Ints(args, "hs"), out string? patrolError), patrolError ?? "无法巡查");
                break;
            case "patrolReward": player.City.FinishPatrol(); break;
            case "playerDestroyBuilding": Require(player.City.DestroyBuilding(args["u"].AsString, out _), "该建筑不可拆除"); player.City.Synchronize(); break;
            case "zhaiRequest":
                var reward = player.City.Rest() ?? throw new InvalidOperationException("当前不能休息");
                player.City.Synchronize();
                player.Notify("zhaiReply", new() { ["rs"] = true, ["reward"] = reward });
                break;
            case "canteenRequest":
                player.City.Canteen(Ints(args, "h"), out string? canteenError);
                Require(canteenError is null, canteenError ?? "深夜食堂不可用");
                player.City.Synchronize();
                break;
            case "incDayWhenNoActionVal":
                if (!player.WeekNum.Advance()) player.WeekNum.ReplaySettlement();
                break;
            case "clearCurDailySettlement": player.WeekNum.ClearSettlement(); break;
        }
    }
}
