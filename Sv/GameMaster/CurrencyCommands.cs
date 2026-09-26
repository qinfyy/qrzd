using Sv.Game;
using Sv.Gateway;
using Sv.Gateway.Packets;

namespace Sv.GameMaster;

public static class CurrencyCommands
{
    public static GameMasterCommand Gold { get; } = new(
        "gold",
        [],
        "设置金币数量。",
        ["/gold <num> [@uid]"],
        ["对应客户端 money 与 updateMoney。"],
        ctx => ExecuteSet(ctx, crystal: false),
        RequireTarget: true);

    public static GameMasterCommand Money { get; } = new(
        "money",
        [],
        "设置晶尘数量。",
        ["/money <num> [@uid]"],
        ["对应客户端 crystal 与 updateCrystal，道具 ID 89；晶钻是 yuanbao（ID 80），不是此命令。"],
        ctx => ExecuteSet(ctx, crystal: true),
        RequireTarget: true);

    private static void ExecuteSet(CommandContext ctx, bool crystal)
    {
        int value = ctx.RequireNonNegativeInt(0, crystal ? "/money <num>" : "/gold <num>");
        ctx.RequireArgCount(1);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        lock (target.SyncRoot)
        {
            if (crystal) target.Profile.Crystal = value;
            else target.Profile.Money = value;
            target.Save();
            if (session?.AvatarEntityId is { } entityId)
            {
                session.SendPack(crystal ? GameMasterStatePacket.Crystal(entityId, value) : GameMasterStatePacket.Money(entityId, value));
            }
        }
        ctx.SendMessage(crystal ? $"晶尘已设为 {value}" : $"金币已设为 {value}");
    }
}
