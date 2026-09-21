using Google.Protobuf;

namespace Sv.Gateway.Packets;

/// <summary>
/// 网关线缆数据包充血基类。
/// </summary>
public abstract class BasePacket
{
    public abstract ushort Method { get; }

    public abstract IMessage CreateMessage();
}
