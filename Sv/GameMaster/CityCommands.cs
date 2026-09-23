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
        "清零当前天数并恢复行动力。",
        ["/clear [@uid]"],
        ["周目保持不变，行动力恢复为 24。"],
        ctx => ExecuteReset(ctx, resetWeek: false),
        RequireTarget: true);

    public static GameMasterCommand ResetWeek { get; } = new(
        "resetweek",
        [],
        "重置周目、天数与行动力。",
        ["/resetweek [@uid]"],
        ["周目设为 1，天数设为 0，行动力恢复为 24。"],
        ctx => ExecuteReset(ctx, resetWeek: true),
        RequireTarget: true);

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

    private static void ExecuteReset(CommandContext ctx, bool resetWeek)
    {
        ctx.RequireArgCount(0);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        lock (target.SyncRoot)
        {
            target.WeekNum.Day = 0;
            if (resetWeek) target.WeekNum.Week = 1;
            target.City.ActionVal = 24;
            target.Save();
            if (session?.AvatarEntityId is { } entityId)
            {
                session.SendPack(GameMasterStatePacket.Week(entityId, target));
                session.SendPack(GameMasterStatePacket.City(entityId, target));
            }
        }
        ctx.SendMessage(resetWeek ? "周目和天数已重置，行动力恢复为 24" : "天数已清零，行动力恢复为 24");
    }
}
