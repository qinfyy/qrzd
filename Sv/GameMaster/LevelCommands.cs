using Sv.Game;

namespace Sv.GameMaster;

/// <summary>
/// 账号等级设置命令。等级同时是事件触发器的条件 7，因此改完必须重算可用事件，
/// 否则会出现“等级已经够了但对应事件始终不出现”。
/// </summary>
public static class LevelCommands
{
    public static GameMasterCommand Level { get; } = new(
        "level",
        ["setlevel", "lv"],
        "设置玩家等级，并立即重算事件触发器。",
        ["/level <num> [@uid]"],
        [
            $"num 为 1..{PlayerProfileLogic.MaxLevel} 的整数，对应策划表 player_exp 的 200 级上限。",
            "等级变更后经验值清零，避免旧经验在下一次获得经验时把等级顶上去。",
            "执行后会重新评估事件条件（条件 7 即玩家等级），新解锁的剧情事件会立即推送。",
            "注意：event_condition 表中条件 7 的取值都是裸数字（如 6000），不是客户端要求的区间写法，" +
                "客户端 cond_table 的 IntervalCondition 同样解析失败。这批条件属于内部测试内容，本就不会触发。",
            "对应客户端 updateExpAndLevel，在线玩家无需重登即可看到等级与升级界面刷新。",
        ],
        ExecuteSetLevel,
        RequireTarget: true);

    private static void ExecuteSetLevel(CommandContext ctx)
    {
        int value = ctx.RequirePositiveInt(0, "/level <num>");
        if (value > PlayerProfileLogic.MaxLevel)
            throw new GameMasterCommandException($"等级超出上限 {PlayerProfileLogic.MaxLevel}");
        ctx.RequireArgCount(1);
        Player target = ctx.GetTargetPlayer();

        int oldLevel;
        string message;
        lock (target.SyncRoot)
        {
            byte[] before = target.SaveToBlob();
            try
            {
                oldLevel = target.Profile.SetLevel(value);
                target.Save();
            }
            catch
            {
                target.Restore(before);
                throw;
            }
            int[] unlocked = target.EventTrigger.Available().Select(row => row.Id).ToArray();
            message = oldLevel == value
                ? $"等级已为 {value}，本次无变化；已重算事件触发器。"
                : $"等级 {oldLevel} -> {value}，经验已清零；已重算事件触发器。";
            if (unlocked.Length > 0) message += $"\n当前可用事件: {string.Join(',', unlocked)}";
        }
        ctx.SendMessage(message);
    }
}
