using System.Buffers.Binary;
using System.Net.Sockets;
using Google.Protobuf;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace Sv.Gateway;

public readonly record struct GatewayFrame(ushort Method, byte[] Payload);

public sealed class GatewayConnection(NetworkStream stream, int maximumFrameBytes)
{
    private readonly Queue<GatewayFrame> pending = new();
    private readonly byte[] wireBuffer = new byte[8192];
    private readonly byte[] inflateBuffer = new byte[8192];
    private readonly byte[] header = new byte[4];
    private int headerCount;
    private byte[]? body;
    private int bodyCount;
    private RC4Engine? encryptor;
    private RC4Engine? decryptor;
    private Deflater? deflater;
    private Inflater? inflater;

    public void EnableEncryption(byte[] key)
    {
        if (encryptor is not null) throw new InvalidOperationException("不能重置会话流密码");
        encryptor = new RC4Engine();
        decryptor = new RC4Engine();
        encryptor.Init(true, new KeyParameter(key));
        decryptor.Init(false, new KeyParameter(key));
    }

    public void EnableCompression()
    {
        if (inflater is not null) throw new InvalidOperationException("不能重置会话压缩流");
        deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, noZlibHeaderOrFooter: false);
        inflater = new Inflater(noHeader: false);
    }

    public async Task<GatewayFrame> ReadAsync(CancellationToken cancellationToken)
    {
        if (inflater is null)
        {
            // 握手逐帧精确读取，不能把紧邻下一阶段的密文按上一阶段解码。
            byte[] prefix = new byte[4];
            await ReadExactlyAsync(prefix, cancellationToken);
            int length = CheckLength(prefix);
            byte[] packet = new byte[length];
            await ReadExactlyAsync(packet, cancellationToken);
            return Decode(packet);
        }

        while (pending.Count == 0)
        {
            int read = await stream.ReadAsync(wireBuffer, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            decryptor?.ProcessBytes(wireBuffer, 0, read, wireBuffer, 0);
            inflater.SetInput(wireBuffer, 0, read);
            int expanded = 0;
            while (true)
            {
                int count = inflater.Inflate(inflateBuffer);
                expanded += count;
                if (expanded > maximumFrameBytes * 4)
                    throw new InvalidDataException("压缩流展开量超过限制");
                Feed(inflateBuffer.AsSpan(0, count));
                if (inflater.IsFinished || inflater.IsNeedingDictionary)
                    throw new InvalidDataException("不支持结束或带外字典的压缩流");
                if (count == 0)
                {
                    if (inflater.IsNeedingInput) break;
                    throw new InvalidDataException("压缩流无法继续解码");
                }
            }
        }
        return pending.Dequeue();
    }

    public async Task SendAsync(ushort method, IMessage message, CancellationToken cancellationToken)
    {
        byte[] payload = message.ToByteArray();
        if (payload.Length + 2 > maximumFrameBytes) throw new InvalidDataException("发送帧过大");
        byte[] packet = new byte[payload.Length + 6];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)(payload.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), method);
        payload.CopyTo(packet, 6);
        if (deflater is not null)
        {
            using MemoryStream compressed = new();
            deflater.SetInput(packet);
            deflater.Flush();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = deflater.Deflate(buffer)) > 0) compressed.Write(buffer, 0, count);
            packet = compressed.ToArray();
        }
        encryptor?.ProcessBytes(packet, 0, packet.Length, packet, 0);
        await stream.WriteAsync(packet, cancellationToken);
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        decryptor?.ProcessBytes(buffer, 0, buffer.Length, buffer, 0);
    }

    private int CheckLength(ReadOnlySpan<byte> bytes)
    {
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (length < 2 || length > maximumFrameBytes)
            throw new InvalidDataException("RPC 帧长度越界");
        return (int)length;
    }

    private static GatewayFrame Decode(byte[] bytes) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(bytes), bytes[2..]);

    private void Feed(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (body is null)
            {
                int count = Math.Min(4 - headerCount, bytes.Length);
                bytes[..count].CopyTo(header.AsSpan(headerCount));
                headerCount += count;
                bytes = bytes[count..];
                if (headerCount < 4) continue;
                body = new byte[CheckLength(header)];
                headerCount = 0;
                bodyCount = 0;
            }
            int consumed = Math.Min(body.Length - bodyCount, bytes.Length);
            bytes[..consumed].CopyTo(body.AsSpan(bodyCount));
            bodyCount += consumed;
            bytes = bytes[consumed..];
            if (bodyCount == body.Length)
            {
                if (pending.Count >= 256) throw new InvalidDataException("待处理 RPC 数量超过限制");
                pending.Enqueue(Decode(body));
                body = null;
            }
        }
    }
}
