using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sv.Configuration;

namespace Sv.Http;

public static class AccountEndpoints
{
    internal const int MaximumBodyBytes = 4096;

    public static void MapAccountEndpoints(this WebApplication app) => app.MapPost("/account", QueryAsync);

    internal static bool VerifySignature(ReadOnlySpan<byte> body, string signature, string key)
    {
        if (signature.Length != 64)
        {
            return false;
        }
        try
        {
            byte[] actual = Convert.FromHexString(signature);
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<IResult> QueryAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        byte[] buffer = new byte[MaximumBodyBytes + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }
            length += read;
        }
        if (length > MaximumBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        ServerOptions options = Config.Server;
        if (!VerifySignature(buffer.AsSpan(0, length), request.Headers["ACCOUNT-SIGNATURE"].ToString(), options.AccountSignatureKey))
        {
            return Results.Json(new { error = "invalid_signature" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        try
        {
            using JsonDocument body = JsonDocument.Parse(buffer.AsMemory(0, length));
            JsonElement root = body.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("an", out JsonElement account) ||
                account.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(account.GetString()) ||
                account.GetString()!.Length > 128 || !root.TryGetProperty("tm", out JsonElement timestamp) ||
                timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out long tm))
            {
                return Results.BadRequest(new { error = "invalid_account_query" });
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (tm < now - options.AccountClockSkewSeconds || tm > now + options.AccountClockSkewSeconds)
            {
                return Results.BadRequest(new { error = "stale_timestamp" });
            }
            // 当前只有 HTTP 框架：没有角色数据库，不能返回抓包中的真实账号或虚构角色。
            return Results.Json(new Dictionary<string, object[]>
            {
                [options.HostId.ToString(CultureInfo.InvariantCulture)] = [],
            });
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "invalid_json" });
        }
    }
}
