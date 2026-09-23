namespace Sv.GameMaster;

public static class OtherCommands
{
    public static GameMasterCommand Help { get; } = new(
        "help",
        ["h"],
        "列出 GM 命令或查看指定命令的详细用法。",
        ["/help", "/help <命令名>"],
        ["游戏内命令默认作用于自己；HTTP 命令需指定 @uid。"],
        ExecuteHelp);

    private static void ExecuteHelp(CommandContext ctx)
    {
        if (ctx.Args.Count > 1) throw new GameMasterCommandException("用法: /help [命令名]");
        if (ctx.GetArg(0) is { } label)
        {
            GameMasterCommand command = GameMasterCommandRegistry.Find(label.TrimStart('/', '!'))
                ?? throw new GameMasterCommandException($"未找到命令: {label}");
            ctx.SendMessage($"{command.Label}{FormatAliases(command)}：{command.Description}");
            ctx.SendMessages(command.Usage);
            ctx.SendMessages(command.Notes.Select(note => $"注: {note}"));
            return;
        }

        ctx.SendMessage($"共 {GameMasterCommandRegistry.Commands.Count} 条命令：");
        foreach (GameMasterCommand command in GameMasterCommandRegistry.Commands)
        {
            ctx.SendMessage($"{command.Label}{FormatAliases(command)}：{command.Description}");
            ctx.SendMessages(command.Usage);
        }
    }

    private static string FormatAliases(GameMasterCommand command) =>
        command.Aliases.Count > 0 ? $"（别名 {string.Join('、', command.Aliases)}）" : string.Empty;
}
