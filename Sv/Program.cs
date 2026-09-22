using Serilog;
using Sv.Configuration;
using Sv.Database;
using Sv.Gateway;
using Sv.Http;
using Sv.Resources;
using Sv.Resources.Tables;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    string contentRootPath = builder.Environment.ContentRootPath;
    Config.Initialize(contentRootPath, builder.Environment.EnvironmentName);
    builder.Configuration.AddConfiguration(Config.Root);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(Config.Root)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(builder.Environment.ContentRootPath, "logs", "sv-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
        .CreateLogger();

    builder.Host.UseSerilog();

    // 初始化数据库单例
    GameDatabase.Initialize(Config.Database);

    // 初始化策划配置表资源
    ResourcesLoader.Initialize(Config.GameData);

    builder.Services.AddSingleton<GatewayHostedService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayHostedService>());

    builder.WebHost.UseUrls($"http://0.0.0.0:{Config.Server.HttpPort}");
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);

    WebApplication app = builder.Build();
    app.UseSerilogRequestLogging();
    app.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        await next(context);
    });

    app.MapGet("/", () => Results.Text("Hello World", "text/plain; charset=utf-8"));
    app.MapDispatchEndpoints();
    app.MapFallback(() => Results.Text($"404 not found", "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound));

    Log.Information("区服 {ServerId} -> {Host}:{GatewayPort}", Config.Server.ServerId, Config.Server.AdvertiseHost, Config.Server.GatewayPort);

    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "QRZD HTTP 服务启动失败");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
