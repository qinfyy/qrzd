using System.IO.Compression;
using MessagePack;
using Sv.Configuration;

namespace Sv.Gateway;

public static class AvatarSnapshot
{
    public static byte[] Create(LocalAccount account, ServerOptions options)
    {
        // 只建立本地新角色的最小初始数据；未恢复的玩法不填入线上快照或虚构进度。
        Dictionary<string, object> snapshot = new()
        {
            ["st"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["an"] = account.Name, ["name"] = account.Name, ["uid"] = account.UserId,
            ["sv"] = account.ServerId, ["hn"] = options.HostId, ["role"] = 1, ["level"] = 1,
            ["weeknum"] = new Dictionary<string, object>
            {
                ["w"] = 1, ["d"] = 0, ["c"] = new Dictionary<string, object>(), ["l"] = new Dictionary<string, object>(),
            },
            ["status"] = new Dictionary<string, object> { ["c"] = "city" },
            ["city"] = new Dictionary<string, object>
            {
                ["dv"] = 0, ["dvc"] = 0, ["av"] = 24, ["bf"] = 0, ["fv"] = 0,
                ["ev"] = 0, ["rv"] = 0, ["areas"] = new Dictionary<string, object>(),
            },
            ["inv"] = new Dictionary<string, object>
            {
                ["mc"] = 100,
                ["items"] = Array.Empty<object>(),
            },
            ["heromgr"] = new Dictionary<string, object>
            {
                ["hrs"] = Array.Empty<object>(),
                ["cfs"] = new Dictionary<string, object>(),
            },
            ["sd"] = new Dictionary<string, object>
            {
                ["sm"] = new Dictionary<string, object>(),
                ["pm"] = new Dictionary<string, object>(),
                ["epm"] = new Dictionary<string, object>(),
                ["fe"] = Array.Empty<object>(),
            },
            ["intelligence"] = new Dictionary<string, object>
            {
                ["is"] = Array.Empty<object>(),
                ["rd"] = false,
            },
        };
        byte[] message = MessagePackSerializer.Serialize(snapshot);
        using MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(message);
        return output.ToArray();
    }
}
