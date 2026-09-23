using System.Text;
using Serilog;
using Sv.Configuration;

namespace Sv.GameMaster;

public static class GameMasterHandbookGenerator
{
    private static readonly ILogger Logger = Log.ForContext(typeof(GameMasterHandbookGenerator));

    public static void Generate(string contentRootPath)
    {
        string outputPath = Path.Combine(contentRootPath, GameMasterOptions.HandbookRelativePath);
        File.WriteAllText(outputPath, BuildContent(), new UTF8Encoding(false));
        Logger.Information("QRZD GM Handbook 已生成: {Path}", outputPath);
    }

    public static string BuildContent()
    {
        StringBuilder builder = new();
        builder.AppendLine("QRZD GM Handbook");
        builder.AppendLine("================");
        builder.AppendLine("游戏内发送 /命令，默认作用于自己；HTTP: GET /api/gm?content=/命令，目标通过 @uid 指定。");
        builder.AppendLine("HTTP 认证：GameMaster:ApiKey 为空时仅允许回环地址；配置后使用 Authorization: Bearer <key>。");
        builder.AppendLine();

        foreach (GameMasterCommand command in GameMasterCommandRegistry.Commands)
        {
            builder.Append(command.Label);
            if (command.Aliases.Count > 0) builder.Append("（别名 ").Append(string.Join('、', command.Aliases)).Append('）');
            builder.Append("：").AppendLine(command.Description);
            foreach (string usage in command.Usage) builder.Append("    ").AppendLine(usage);
            foreach (string note in command.Notes) builder.Append("  - ").AppendLine(note);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
