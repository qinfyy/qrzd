using Sv.Game;
using Sv.Gateway;
using Sv.Gateway.Packets;

namespace Sv.GameMaster;

public static class CityCommands
{
    public static GameMasterCommand Action { get; } = new(
        "action",
        [],
        "设置交界都市剩余行动力。",
        ["/action <num> [@uid]"],
        ["num 为非负整数。"],
        ExecuteAction,
        RequireTarget: true);

    public static GameMasterCommand Clear { get; } = new(
        "clear",
        [],
        "重置首周主线到原版序章。",
        ["/clear [@uid]"],
        ["与 resetweek 一致，清除本轮剧情、城市、引导、战斗和消息状态；神器使只重置本周好感、疲劳和剧情限制，保留已获得神器使及养成、账号资产与历史结局。需要重新登录。"],
        ExecuteReset,
        RequireTarget: true);

    public static GameMasterCommand ResetWeek { get; } = new(
        "resetweek",
        [],
        "一致重置首周主线到原版序章。",
        ["/resetweek [@uid]"],
        ["内部周目为 0，天数为 0，行动力为 24。清除本轮主线状态；神器使只重置本周好感、疲劳和剧情限制，保留已获得神器使及养成、账号资产与历史结局。需要重新登录。"],
        ExecuteReset,
        RequireTarget: true);

    public static GameMasterCommand Status { get; } = new(
        "citystatus", [], "查看首周主线进度及事件阻塞条件。", ["/citystatus [事件ID] [@uid]"], [], ExecuteStatus, RequireTarget: true);

    private static void ExecuteStatus(CommandContext ctx)
    {
        if (ctx.Args.Count > 1) throw new GameMasterCommandException("用法: /citystatus [事件ID] [@uid]");
        Player target = ctx.GetTargetPlayer();
        lock (target.SyncRoot)
        {
            ctx.SendMessage($"UID={target.Uid} 内部周目={target.WeekNum.Week} 显示周目={target.WeekNum.Week + 1} day={target.WeekNum.Day} route={target.Story.Route}");
            ctx.SendMessage($"状态={target.Status.CurrentStatus} emergency={target.Status.Emergency} 行动力={target.City.ActionVal} 跨日检查={target.WeekNum.CanAdvance()}");
            ctx.SendMessage($"进行中事件: {string.Join(',', target.EventTrigger.Processing)}; 可用事件: {string.Join(',', target.EventTrigger.Available().Select(row => row.Id))}");
            ctx.SendMessage($"战斗={target.Combat.Active?.Stage ?? 0} 结局={target.WeekNum.EndingId} 已完成事件={target.EventTrigger.Completed.Count}");
            if (ctx.Args.Count == 1) ctx.SendMessages(target.EventTrigger.BlockingConditions(ctx.RequireNonNegativeInt(0, "/citystatus [事件ID]")));
        }
    }

    private static void ExecuteAction(CommandContext ctx)
    {
        int value = ctx.RequireNonNegativeInt(0, "/action <num>");
        ctx.RequireArgCount(1);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        lock (target.SyncRoot)
        {
            target.City.ActionVal = value;
            target.Save();
            if (session?.AvatarEntityId is { } entityId) session.SendPack(GameMasterStatePacket.City(entityId, target));
        }
        ctx.SendMessage($"行动力已设为 {value}");
    }

    private static void ExecuteReset(CommandContext ctx)
    {
        ctx.RequireArgCount(0);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        lock (target.SyncRoot)
        {
            target.WeekNum.ResetMainline();
            target.Save();
        }
        ctx.SendMessage("首周主线已重置，请重新登录；已获得神器使及养成、账号资产与历史结局保留。");
        session?.Close();
    }
}
