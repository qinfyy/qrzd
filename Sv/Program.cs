using Serilog;
using Sv.Configuration;
using Sv.Database;
using Sv.Gateway;
using Sv.Http;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    string contentRootPath = builder.Environment.ContentRootPath;
    Config.Initialize(builder.Configuration, contentRootPath);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(builder.Environment.ContentRootPath, "logs", "sv-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
        .CreateLogger();

    builder.Host.UseSerilog();

    // 初始化数据库单例
    GameDatabase.Initialize(Config.Database);

    builder.Services.AddSingleton<GatewayRouter>();
    builder.Services.AddHostedService<GatewayHostedService>();

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
    app.MapFallback(() => Results.Json(new { error = "not_found" }, statusCode: 404));

    Log.Information("QRZD HTTP 监听 {Port}；发布区服 {ServerId} -> {Host}:{GatewayPort}；基础 Gateway 登录启用",
        Config.Server.HttpPort, Config.Server.ServerId, Config.Server.AdvertiseHost, Config.Server.GatewayPort);

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
