using System.Net;
using System.Text;

namespace Sv.Configuration;

public sealed class ServerOptions
{
    public const string SectionName = "Server";
    public int HttpPort { get; init; } = 21000;
    public string AdvertiseHost { get; init; } = "127.0.0.1";

    public int GatewayPort { get; init; } = 4120;
    public int Port { get => GatewayPort; init => GatewayPort = value; }

    public string GatewayPrivateKeyPath { get; init; } = "ServerData/gateway_private.pem";
    public int GatewayMaxConnections { get; init; } = 32;
    public int GatewayMaxFrameBytes { get; init; } = 131072;
    public string TimeZone { get; init; } = "China Standard Time";
    public bool EnableNewbieTutorial { get; init; } = true;
    public int GatewayHandshakeSeconds { get; init; } = 30;
    public int GatewayIdleSeconds { get; init; } = 120;
    public int HostId { get; init; } = 10003;
    public int ServerId { get; init; } = 5004;
    public string ServerName { get; init; } = "本地测试服";
    public string AnnouncementTitle { get; init; } = "本地测试";
    public string AnnouncementText { get; init; } = "#s30QRZD 本地测试服#r#s24已实现进门";
    public string AccountSignatureKey { get; init; } = "xxlr5ob%g(q!6*8wiu8mz)uz3lu5y^&y";
    public int AccountClockSkewSeconds { get; init; } = 300;

    public static ServerOptions FromConfiguration(IConfiguration configuration)
    {
        ServerOptions options = configuration.GetRequiredSection(SectionName).Get<ServerOptions>()
            ?? throw new InvalidOperationException("无法读取 Server 配置");
        options.Validate();
        return options;
    }

    internal void Validate()
    {
        if (HttpPort is < 1 or > 65535 || GatewayPort is < 1 or > 65535 || HttpPort == GatewayPort)
        {
            throw new InvalidOperationException("HTTP/Gateway 端口必须有效且不同");
        }
        if (!IPAddress.TryParse(AdvertiseHost, out IPAddress? address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("AdvertiseHost 必须是模拟器可达的本地 IPv4 地址");
        }
        if (!IsLocalAddress(address))
        {
            throw new InvalidOperationException("本地调试网关只允许回环或 RFC1918 地址");
        }
        if (GatewayMaxConnections is < 1 or > 256 || GatewayMaxFrameBytes is < 1024 or > 1048576 ||
            GatewayHandshakeSeconds is < 1 or > 300 || GatewayIdleSeconds is < 10 or > 3600)
        {
            throw new InvalidOperationException("Gateway 容量、帧大小或超时配置无效");
        }
        if (string.IsNullOrWhiteSpace(GatewayPrivateKeyPath))
        {
            throw new InvalidOperationException("GatewayPrivateKeyPath 不能为空");
        }
        if (HostId <= 0 || ServerId <= 0 || string.IsNullOrWhiteSpace(ServerName) ||
            ServerName.Any(char.IsWhiteSpace) || ServerName.Contains('#'))
        {
            throw new InvalidOperationException("区服 ID 必须为正整数，名称不能含空白或 #");
        }
        if (string.IsNullOrWhiteSpace(AnnouncementTitle) || string.IsNullOrWhiteSpace(AnnouncementText))
        {
            throw new InvalidOperationException("公告标题和内容不能为空");
        }
        if (Encoding.UTF8.GetByteCount(AccountSignatureKey) < 16 || AccountClockSkewSeconds is < 1 or > 86400)
        {
            throw new InvalidOperationException("Account 签名密钥或时间窗口无效");
        }
    }

    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        byte[] ip = address.GetAddressBytes();
        return ip[0] is 10 or 127 || ip[0] == 192 && ip[1] == 168 || ip[0] == 172 && ip[1] is >= 16 and <= 31;
    }
}
