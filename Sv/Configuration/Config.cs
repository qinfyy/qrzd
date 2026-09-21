namespace Sv.Configuration;

public static class Config
{
    public static ServerOptions Server { get; private set; } = new();
    public static DatabaseOptions Database { get; private set; } = new();
    public static AccountOptions Account { get; private set; } = new();
    public static GameDataOptions GameData { get; private set; } = new();
    public static GameMasterOptions GameMaster { get; private set; } = new();

    public static void Initialize(IConfiguration configuration, string contentRootPath)
    {
        Server = ServerOptions.FromConfiguration(configuration);
        Database = configuration.GetSection(DatabaseOptions.SectionName).Exists()
            ? DatabaseOptions.FromConfiguration(configuration, contentRootPath)
            : new DatabaseOptions { Path = Path.Combine(contentRootPath, "data", "SaveData.db") };
        Account = AccountOptions.FromConfiguration(configuration);
        GameData = configuration.GetSection(GameDataOptions.SectionName).Exists()
            ? GameDataOptions.FromConfiguration(configuration, contentRootPath)
            : new GameDataOptions();
        GameMaster = GameMasterOptions.FromConfiguration(configuration);
    }
}
