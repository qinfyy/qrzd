using Google.Protobuf;
using Mobile.Server;
using MongoDB.Bson;
using Sv.Gateway.Protocol;

namespace Sv.Gateway.Packets;

public sealed class AvatarRpcPacket : BasePacket
{
    private readonly ByteString _entity;
    public string Name { get; }
    public BsonDocument Arguments { get; }

    public AvatarRpcPacket(ByteString entity, string method, BsonDocument arguments)
    {
        _entity = entity;
        Name = method;
        Arguments = arguments.DeepClone().AsBsonDocument;
    }

    public AvatarRpcPacket(ByteString entity, string method, Dictionary<string, object> arguments) : this(entity, method, BsonHelper.ToBsonVal(arguments).AsBsonDocument) { }

    public override ushort Method => 5;
    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = _entity, Method = AeadTool.EncodeMethodName(Name), Parameters = ByteString.CopyFrom(Arguments.ToBson()),
    };
}
