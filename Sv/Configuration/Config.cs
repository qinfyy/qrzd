namespace Sv.Configuration;

public static class Config
{
    public static ServerOptions Server { get; private set; } = new();

    public static void Initialize(IConfiguration configuration)
    {
        Server = ServerOptions.FromConfiguration(configuration);
    }
}
