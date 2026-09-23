using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Sv.Configuration;
using Sv.GameMaster;

namespace Sv.Http;

public static class GameMasterEndpoints
{
    public static void MapGameMasterEndpoints(this WebApplication app)
    {
        app.MapGet("/api/gm", (HttpContext context, GameMasterService service, ILogger<GameMasterService> logger) =>
        {
            if (!IsAuthorized(context)) return Results.Json(new { success = false, errorDescription = "unauthorized" }, statusCode: 401);

            string? content = context.Request.Query["content"];
            if (string.IsNullOrWhiteSpace(content))
            {
                return Results.Json(new { success = false, errorDescription = "缺少参数: content" }, statusCode: 400);
            }

            try
            {
                return Results.Json(new { success = true, messages = service.Execute(content) });
            }
            catch (Exception exception) when (exception is GameMasterCommandException or ArgumentOutOfRangeException or OverflowException)
            {
                return Results.Json(new { success = false, errorDescription = exception.Message }, statusCode: 400);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "GM 命令执行失败");
                return Results.Json(new { success = false, errorDescription = "internal_error" }, statusCode: 500);
            }
        });
    }

    private static bool IsAuthorized(HttpContext context)
    {
        string key = Config.GameMaster.ApiKey;
        if (string.IsNullOrEmpty(key)) return context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);

        string authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        string token = authorization[7..];
        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
