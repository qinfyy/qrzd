namespace Sv.Configuration;

public sealed class AccountOptions
{
    public const string SectionName = "Account";

    public bool AutoCreate { get; init; } = true;

    public bool VerifyPassword { get; init; } = true;

    public static AccountOptions FromConfiguration(IConfiguration configuration) => configuration.GetSection(SectionName).Get<AccountOptions>() ?? new AccountOptions();
}
