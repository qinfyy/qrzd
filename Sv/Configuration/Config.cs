using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Sv.Configuration;

/// <summary>
/// 服务器配置入口：自建 ConfigurationBuilder 并缓存各段选项，静态类可直接读取 Config.*。
/// </summary>
public static class Config
{
    private const string AppSettingsFileName = "appsettings.json";

    private static IConfigurationRoot? _root;

    /// <summary>原始配置树，供 Serilog 等按 IConfiguration 读取的组件使用。</summary>
    public static IConfigurationRoot Root => _root ?? throw new InvalidOperationException("Config 尚未初始化");

    public static ServerOptions Server { get; private set; } = new();
    public static DatabaseOptions Database { get; private set; } = new();
    public static AccountOptions Account { get; private set; } = new();
    public static GameDataOptions GameData { get; private set; } = new();
    public static GameMasterOptions GameMaster { get; private set; } = new();

    /// <summary>读取 appsettings.json 与 appsettings.{环境}.json；主配置文件缺失时生成默认配置。</summary>
    public static void Initialize(string contentRootPath, string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        string appSettingsPath = Path.Combine(contentRootPath, AppSettingsFileName);
        if (!File.Exists(appSettingsPath))
        {
            File.WriteAllText(appSettingsPath, GenerateDefaultAppSettings());
            Log.Logger.Warning("appsettings.json 不存在，已生成默认配置 {Path}", appSettingsPath);
        }

        _root = new ConfigurationBuilder()
            .SetBasePath(contentRootPath)
            .AddJsonFile(AppSettingsFileName, optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
            .Build();

        Server = ServerOptions.FromConfiguration(_root);
        Database = _root.GetSection(DatabaseOptions.SectionName).Exists()
            ? DatabaseOptions.FromConfiguration(_root, contentRootPath)
            : new DatabaseOptions { Path = Path.Combine(contentRootPath, "save", "SaveData.db") };
        Account = AccountOptions.FromConfiguration(_root);
        GameData = _root.GetSection(GameDataOptions.SectionName).Exists()
            ? GameDataOptions.FromConfiguration(_root, contentRootPath)
            : new GameDataOptions();
        GameMaster = GameMasterOptions.FromConfiguration(_root);
    }

    private static string GenerateDefaultAppSettings() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["Serilog"] = new Dictionary<string, object?>
        {
            ["MinimumLevel"] = new Dictionary<string, object?>
            {
                ["Default"] = "Information",
                ["Override"] = new Dictionary<string, string?>
                {
                    ["Microsoft.AspNetCore"] = "Warning",
                    ["Microsoft.EntityFrameworkCore"] = "Warning",
                },
            },
        },
        ["Server"] = new ServerOptions(),
        ["Database"] = new DatabaseOptions(),
        ["Account"] = new AccountOptions(),
        ["GameData"] = new GameDataOptions(),
        ["GameMaster"] = new GameMasterOptions(),
    }, new JsonSerializerOptions { WriteIndented = true });
}
