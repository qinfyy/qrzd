namespace Sv.Configuration;

public sealed class GameMasterOptions
{
    public const string SectionName = "GameMaster";
    public const string HandbookRelativePath = "Handbook.txt";

    public string ApiKey { get; init; } = string.Empty;

    public static GameMasterOptions FromConfiguration(IConfiguration configuration)
    {
        return configuration.GetSection(SectionName).Get<GameMasterOptions>() ?? new GameMasterOptions();
    }
}
