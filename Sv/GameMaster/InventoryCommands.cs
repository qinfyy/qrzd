using Sv.Database;
using Sv.Game;
using Sv.Gateway;
using Sv.Gateway.Packets;

namespace Sv.GameMaster;

public static class InventoryCommands
{
    public static GameMasterCommand Item { get; } = new(
        "item",
        [],
        "向背包发放指定道具。",
        ["/item <id> <cnt> [@uid]"],
        ["仅接受当前资源表中的可堆叠道具，单次最多发放 100 组。"],
        ExecuteItem,
        RequireTarget: true);

    private static void ExecuteItem(CommandContext ctx)
    {
        int itemId = ctx.RequirePositiveInt(0, "/item <id> <cnt>");
        int count = ctx.RequirePositiveInt(1, "/item <id> <cnt>");
        ctx.RequireArgCount(2);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        lock (target.SyncRoot)
        {
            ItemState[] items = target.Inventory.Grant(itemId, count);
            target.Save();
            if (session?.AvatarEntityId is { } entityId) session.SendPack(GameMasterStatePacket.Items(entityId, items));
        }
        ctx.SendMessage($"已发放道具 {itemId} x{count}");
    }
}
