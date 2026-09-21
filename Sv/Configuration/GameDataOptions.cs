namespace Sv.Configuration;

public sealed class GameDataOptions
{
    public const string SectionName = "GameData";

    /// <summary>客户端资源表根目录（解包的 Data 目录），TableTools TSV/JSON 从这里加载。</summary>
    public string TableRoot { get; init; } = string.Empty;

    /// <summary>
    /// 服务端自建资源目录（LevelChooseSchedule/Store/Gacha 等 TSV）。
    /// 与 TableRoot 分离：服务端数据不与客户端解包资源混放。
    /// 缺省回退到 AppContext.BaseDirectory\ServerData（兼容旧部署）。
    /// </summary>
    public string ServerDataPath { get; init; } = string.Empty;

    public static GameDataOptions FromConfiguration(IConfiguration configuration, string contentRootPath)
    {
        GameDataOptions options = configuration.GetRequiredSection(SectionName).Get<GameDataOptions>() ?? throw new InvalidOperationException($"无法加载 {SectionName} 配置");
        return new GameDataOptions
        {
            TableRoot = string.IsNullOrWhiteSpace(options.TableRoot) ? string.Empty : Path.GetFullPath(options.TableRoot, contentRootPath),
            ServerDataPath = string.IsNullOrWhiteSpace(options.ServerDataPath) ? string.Empty : Path.GetFullPath(options.ServerDataPath, contentRootPath),
        };
    }
}
