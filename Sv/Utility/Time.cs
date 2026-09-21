using Sv.Configuration;

namespace Sv.Utility;

/// <summary>服务器统一时间入口：DateTimeOffset 的生成与解析都按配置时区（Server:TimeZone）。</summary>
public static class Time
{
    /// <summary>时间列的 Unix 秒表示（uint），未设置返回 0。</summary>
    public static uint ToUnixSeconds(DateTimeOffset? value) => value is { } time ? checked((uint)time.ToUnixTimeSeconds()) : 0u;

    /// <summary>配置时区；未配置、空串或解析失败时回退系统本地时区。</summary>
    public static TimeZoneInfo TimeZone { get; } = ResolveTimeZone(Config.Server.TimeZone);

    /// <summary>当前时刻，按配置时区。</summary>
    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZone);

    /// <summary>把任意时刻换算到配置时区。</summary>
    public static DateTimeOffset InTimeZone(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone);

    /// <summary>Unix 秒按配置时区还原为 DateTimeOffset。</summary>
    public static DateTimeOffset FromUnixSeconds(long seconds) => InTimeZone(DateTimeOffset.FromUnixTimeSeconds(seconds));

    /// <summary>给不含时区信息的时间文本挂上配置时区偏移（DateTimeKind.Local 会挂成系统时区，不能直接用）。</summary>
    public static DateTimeOffset FromLocal(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeZone.GetUtcOffset(value));

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }
}
