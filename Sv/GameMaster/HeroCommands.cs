using Sv.Game;
using Sv.Gateway;
using Sv.Gateway.Packets;

namespace Sv.GameMaster;

public static class HeroCommands
{
    public static GameMasterCommand Unlock { get; } = new(
        "hero",
        [],
        "解锁指定神器使。",
        ["/hero <id> [@uid]"],
        ["已拥有的神器使不会重复发放。"],
        ExecuteUnlock,
        RequireTarget: true);

    public static GameMasterCommand UnlockAll { get; } = new(
        "allhero",
        ["@allhero"],
        "补齐资源表中的全部神器使。",
        ["@allhero [@uid]", "/allhero [@uid]"],
        ["只对新解锁的神器使发送在线增量。"],
        ExecuteUnlockAll,
        RequireTarget: true);

    private static void ExecuteUnlock(CommandContext ctx)
    {
        int heroId = ctx.RequirePositiveInt(0, "/hero <id>");
        ctx.RequireArgCount(1);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        bool added;
        lock (target.SyncRoot)
        {
            added = target.HeroMgr.Unlock(heroId);
            target.Save();
            if (added && session?.AvatarEntityId is { } entityId)
            {
                session.SendPack(GameMasterStatePacket.Hero(entityId, target, heroId));
            }
        }
        ctx.SendMessage(added ? $"已解锁神器使 {heroId}" : $"神器使 {heroId} 已解锁");
    }

    private static void ExecuteUnlockAll(CommandContext ctx)
    {
        ctx.RequireArgCount(0);
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        int added;
        lock (target.SyncRoot)
        {
            HashSet<int> existing = target.HeroMgr.HeroIds.ToHashSet();
            added = target.HeroMgr.UnlockAll();
            target.Save();
            if (added > 0 && session?.AvatarEntityId is { } entityId)
            {
                foreach (int heroId in target.HeroMgr.HeroIds)
                {
                    if (!existing.Contains(heroId)) session.SendPack(GameMasterStatePacket.Hero(entityId, target, heroId));
                }
            }
        }
        ctx.SendMessage($"新增 {added} 名神器使");
    }
}
