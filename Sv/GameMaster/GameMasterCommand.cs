namespace Sv.GameMaster;

public sealed record GameMasterCommand(
    string Label,
    IReadOnlyList<string> Aliases,
    string Description,
    IReadOnlyList<string> Usage,
    IReadOnlyList<string> Notes,
    Action<CommandContext> Execute,
    bool RequireTarget = false,
    bool RequireTargetOnline = false);

public static class GameMasterCommandRegistry
{
    public static IReadOnlyList<GameMasterCommand> Commands { get; } =
    [
        CityCommands.Action,
        GiveCommands.Give,
        GiveCommands.GiveAll,
        HeroCommands.SetHero,
        CurrencyCommands.Gold,
        CurrencyCommands.Money,
        LevelCommands.Level,
        CityCommands.Clear,
        CityCommands.ResetWeek,
        CityCommands.Status,
        OtherCommands.Help,
    ];

    public static GameMasterCommand? Find(string label) => Commands.FirstOrDefault(command =>
        string.Equals(command.Label, label, StringComparison.OrdinalIgnoreCase) ||
        command.Aliases.Contains(label, StringComparer.OrdinalIgnoreCase));
}
