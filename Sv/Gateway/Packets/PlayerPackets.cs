using Google.Protobuf;
using MongoDB.Bson;
using Sv.Configuration;
using Sv.Game;
using Sv.Gateway.Protocol;
using Mobile.Server;

namespace Sv.Gateway.Packets;

public sealed class ClientAvatarPacket(ByteString avatarEntityId, Player player, ServerOptions options) : BasePacket
{
    public override ushort Method => 3;

    public override IMessage CreateMessage() => new EntityInfo
    {
        Id = avatarEntityId,
        Type = AeadTool.EncodeMethodName("ClientAvatar"),
        Info = ByteString.CopyFrom(player.ToAvatarSnapshot(options)),
    };
}

public sealed class BecomePlayerPacket(ByteString avatarEntityId) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("become_player"),
        Parameters = ByteString.CopyFrom(new BsonDocument().ToBson()),
    };
}

public sealed class HeartbeatAckPacket(ByteString avatarEntityId) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("on_heartbeat"),
        Parameters = ByteString.CopyFrom(new BsonDocument().ToBson()),
    };
}

public sealed class SyncServerTimePacket(ByteString avatarEntityId, long unixSeconds) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("onSyncServerTime"),
        Parameters = ByteString.CopyFrom(new BsonDocument { ["t"] = (int)unixSeconds }.ToBson()),
    };
}

public sealed class PullEventsReplyPacket(ByteString avatarEntityId, int cbid = 0) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage()
    {
        BsonDocument reply = new()
        {
            ["a"] = new BsonArray(),
            ["p"] = new BsonArray(),
        };
        if (cbid != 0)
        {
            reply["_cbid_"] = cbid;
        }
        return new EntityMessage
        {
            Id = avatarEntityId,
            Method = AeadTool.EncodeMethodName("pullEventsReply"),
            Parameters = ByteString.CopyFrom(reply.ToBson()),
        };
    }
}

public sealed class HeroStarOrderReplyPacket(ByteString avatarEntityId, int heroId) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("incHeroStarOrder"),
        Parameters = ByteString.CopyFrom(new BsonDocument { ["h"] = heroId, ["artifact"] = false }.ToBson()),
    };
}

public sealed class ReliableRpcAckPacket(ByteString avatarEntityId, int rpcSeq) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("reliableRpcAck"),
        Parameters = ByteString.CopyFrom(new BsonDocument { ["s"] = rpcSeq }.ToBson()),
    };
}

public sealed class SyncAllIntelligencePacket(ByteString avatarEntityId, bool readed) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("syncAllIntelligence"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["is"] = new BsonDocument
            {
                ["is"] = new BsonArray(),
                ["rd"] = readed,
            },
        }.ToBson()),
    };
}

public sealed class PullEventsRequestPacket
{
    public int Cbid { get; init; }

    public static PullEventsRequestPacket FromBson(BsonDocument args) =>
        new() { Cbid = args.GetValue("_cbid_", 0).AsInt32 };
}

public sealed class HeroStarOrderRequestPacket
{
    public int HeroId { get; init; }

    public static HeroStarOrderRequestPacket? FromBson(BsonDocument args)
    {
        if (!args.TryGetValue("h", out BsonValue? heroId) || !heroId.IsInt32 ||
            !args.TryGetValue("cost", out BsonValue? cost) || !cost.IsBsonArray || cost.AsBsonArray.Count != 0 ||
            !args.TryGetValue("fkt", out BsonValue? fakeTreasure) || !fakeTreasure.IsInt32 || fakeTreasure.AsInt32 != 0)
        {
            return null;
        }

        return new HeroStarOrderRequestPacket { HeroId = heroId.AsInt32 };
    }
}

public sealed class ReliableRpcRequestPacket
{
    public string? SubMethod { get; init; }
    public int RpcSeq { get; init; }
    public int Cbid { get; init; }
    public BsonDocument? Parameters { get; init; }

    public static ReliableRpcRequestPacket? FromBson(BsonDocument args)
    {
        if (!args.TryGetValue("w", out BsonValue? wVal) || !wVal.IsBsonDocument)
        {
            return null;
        }

        BsonDocument wrapper = wVal.AsBsonDocument;
        string? subMethod = wrapper.GetValue("m", null)?.AsString;
        int rpcSeq = wrapper.GetValue("r", 0).AsInt32;
        int cbid = args.GetValue("_cbid_", 0).AsInt32;
        BsonDocument? parameters = null;

        if (wrapper.TryGetValue("p", out BsonValue? pVal) && pVal.IsBsonDocument)
        {
            parameters = pVal.AsBsonDocument;
            if (cbid == 0)
            {
                cbid = parameters.GetValue("_cbid_", 0).AsInt32;
            }
        }

        return new ReliableRpcRequestPacket
        {
            SubMethod = subMethod,
            RpcSeq = rpcSeq,
            Cbid = cbid,
            Parameters = parameters,
        };
    }
}
