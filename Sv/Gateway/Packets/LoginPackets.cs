using Google.Protobuf;
using MongoDB.Bson;
using Sv.Gateway.Protocol;
using Mobile.Server;
using ProtoVoid = Mobile.Server.Void;

namespace Sv.Gateway.Packets;

public sealed class SessionSeedReplyPacket(long seed) : BasePacket
{
    public override ushort Method => 0;

    public override IMessage CreateMessage() => new SessionSeed { Seed = seed };
}

public sealed class SessionKeyOkPacket : BasePacket
{
    public override ushort Method => 1;

    public override IMessage CreateMessage() => new ProtoVoid();
}

public sealed class ConnectServerReplyPacket(ConnectServerReply.Types.ReplyType replyType) : BasePacket
{
    public override ushort Method => 2;

    public override IMessage CreateMessage() => new ConnectServerReply { Type = replyType };
}

public sealed class ClientAccountPacket(ByteString accountEntityId) : BasePacket
{
    public override ushort Method => 3;

    public override IMessage CreateMessage() => new EntityInfo
    {
        Id = accountEntityId,
        Type = AeadTool.EncodeMethodName("ClientAccount"),
        Info = ByteString.CopyFrom(new BsonDocument().ToBson()),
    };
}

public sealed class LoginFailPacket(ByteString accountEntityId, string reason) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = accountEntityId,
        Method = AeadTool.EncodeMethodName("onLoginFail"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["e"] = 1,
            ["m"] = reason,
        }.ToBson()),
    };
}

public sealed class LoginRequestPacket
{
    public string Name { get; init; } = string.Empty;
    public int ServerId { get; init; }
    public string Password { get; init; } = string.Empty;

    public static LoginRequestPacket? FromBson(BsonDocument args)
    {
        if (!args.TryGetValue("name", out BsonValue? nameVal) || !nameVal.IsString ||
            !args.TryGetValue("sv", out BsonValue? svVal) || !svVal.IsInt32 ||
            !args.TryGetValue("psw", out BsonValue? pswVal) || !pswVal.IsString)
        {
            return null;
        }

        return new LoginRequestPacket
        {
            Name = nameVal.AsString,
            ServerId = svVal.AsInt32,
            Password = pswVal.AsString,
        };
    }
}
