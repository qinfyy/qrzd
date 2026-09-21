namespace Sv.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Path { get; init; } = "save/SaveData.db";

    public static DatabaseOptions FromConfiguration(IConfiguration configuration, string contentRootPath)
    {
        DatabaseOptions options = configuration.GetRequiredSection(SectionName).Get<DatabaseOptions>() ?? throw new InvalidOperationException($"无法加载 {SectionName} 配置");
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new InvalidOperationException("Database:Path 不能为空");
        }

        return new DatabaseOptions
        {
            Path = System.IO.Path.GetFullPath(options.Path, contentRootPath),
        };
    }
}
