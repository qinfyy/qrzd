using Sv.Game;

namespace Sv.GameMaster;

/// <summary>
/// 神器使养成设置命令。与 give 的分工：give 负责发放角色实体（give hero / giveall hero），
/// hero 只改写玩家已拥有角色的养成字段，对应客户端 HeroMgrLogic 的各个养成 RPC。
/// </summary>
public static class HeroCommands
{
    public static GameMasterCommand SetHero { get; } = new(
        "hero",
        ["sethero"],
        "设置已拥有神器使的养成字段。发放角色请用 give hero，批量补齐用 giveall hero。",
        [
            "/hero <id> [sl星级] [so小阶] [art神器等级] [aw觉醒] [lib解放] [@uid]",
            "/hero <id> -max [@uid]",
        ],
        [
            "角色必须已被玩家拥有；未拥有时请先执行 /give hero <id>。",
            "sl<星级> 为 1..4（异界体 1..5），so<小阶> 为 1..4，超出范围按上限截断。",
            "art<神器等级> 会同时置为已开启神器状态；神器等级上限受账号等级约束，这里按资源上限截断。",
            "aw<觉醒> 为 0..4，需要该角色在 hero.json 的 open_awake 非 0，否则该参数被忽略。",
            "lib<解放> 为 0..解放阶段上限，超出按上限截断。",
            "-max 一次性把星级、神器等级、觉醒与解放拉满，用于快速验证养成闭环。",
            "本命令直接改写存档，不消耗碎片与材料；在线玩家提交后会收到该角色的完整同步。",
        ],
        ExecuteSetHero,
        RequireTarget: true);

    private static void ExecuteSetHero(CommandContext ctx)
    {
        int heroId = ctx.RequirePositiveInt(0, "/hero <id>");
        int? starLevel = null;
        int? starOrder = null;
        int? artifactLevel = null;
        int? awakeStage = null;
        int? liberateStage = null;
        bool max = false;

        for (int i = 1; i < ctx.Args.Count; i++)
        {
            if (ctx.Args[i].StartsWith('@')) continue;
            string token = ctx.Args[i].ToLowerInvariant();
            if (token == "-max") { max = true; continue; }
            if (token.StartsWith("sl", StringComparison.Ordinal)) starLevel = ParseValue(token[2..], "sl");
            else if (token.StartsWith("so", StringComparison.Ordinal)) starOrder = ParseValue(token[2..], "so");
            else if (token.StartsWith("art", StringComparison.Ordinal)) artifactLevel = ParseValue(token[3..], "art");
            else if (token.StartsWith("aw", StringComparison.Ordinal)) awakeStage = ParseValue(token[2..], "aw");
            else if (token.StartsWith("lib", StringComparison.Ordinal)) liberateStage = ParseValue(token[3..], "lib");
            else throw new GameMasterCommandException($"无法识别参数 {ctx.Args[i]}；使用 sl/so/art/aw/lib 或 -max");
        }

        if (max)
        {
            starLevel = int.MaxValue;
            starOrder = HeroMgrLogic.MaxStarOrder;
            artifactLevel = int.MaxValue;
            awakeStage = HeroMgrLogic.MaxAwakeStage;
            liberateStage = int.MaxValue;
        }
        else if (starLevel is null && starOrder is null && artifactLevel is null && awakeStage is null && liberateStage is null)
        {
            throw new GameMasterCommandException("未指定任何养成字段；使用 sl/so/art/aw/lib 或 -max");
        }

        Player target = ctx.GetTargetPlayer();
        string message;
        lock (target.SyncRoot)
        {
            if (target.HeroMgr.Find(heroId) is null)
                throw new GameMasterCommandException($"神器使 {heroId} 未解锁；请先执行 /give hero {heroId}");

            target.HeroMgr.ApplyProgression(heroId, starLevel, starOrder, artifactLevel, awakeStage, liberateStage);
            target.Save();
            message = $"已更新神器使 {heroId} 养成：{target.HeroMgr.DescribeProgression(heroId)}";
        }
        target.HeroMgr.Synchronize();
        ctx.SendMessage(message);
    }

    private static int ParseValue(string text, string name) =>
        int.TryParse(text, out int value) && value >= 0
            ? value
            : throw new GameMasterCommandException($"{name} 必须为非负整数");
}
