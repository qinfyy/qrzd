using Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Sv.Gateway.Protocol;
using Mobile.Server;

namespace Sv.Gateway.Packets;

internal static class BsonHelper
{
    public static BsonValue ToBsonVal(object? obj)
    {
        if (obj is null) return BsonNull.Value;
        if (obj is BsonValue bv) return bv;
        if (obj is int i) return new BsonInt32(i);
        if (obj is long l) return new BsonInt64(l);
        if (obj is string s) return new BsonString(s);
        if (obj is bool b) return new BsonBoolean(b);
        if (obj is double d) return new BsonDouble(d);
        if (obj is IDictionary<string, object> dict)
        {
            BsonDocument doc = new();
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                doc[kvp.Key] = ToBsonVal(kvp.Value);
            }
            return doc;
        }
        if (obj is System.Collections.IEnumerable list)
        {
            BsonArray arr = new();
            foreach (object item in list)
            {
                arr.Add(ToBsonVal(item));
            }
            return arr;
        }
        return BsonTypeMapper.MapToBsonValue(obj);
    }
}

public sealed class GetAllAreaInfoReplyPacket(ByteString avatarEntityId, Dictionary<string, object> areasInfo, Dictionary<string, object> cityData, bool settlement = false) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("get_all_area_info_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["ais"] = BsonHelper.ToBsonVal(areasInfo),
            ["cd"] = BsonHelper.ToBsonVal(cityData),
            ["sm"] = settlement,
        }.ToBson()),
    };
}

public sealed class GetAreaInfoReplyPacket(ByteString avatarEntityId, Dictionary<string, object> areaInfo) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("get_area_info_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["ai"] = BsonHelper.ToBsonVal(areaInfo),
        }.ToBson()),
    };
}

public sealed class GetCityDataReplyPacket(ByteString avatarEntityId, Dictionary<string, object> cityData) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("get_city_data_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["cd"] = BsonHelper.ToBsonVal(cityData),
        }.ToBson()),
    };
}

public sealed class GetAreaStagesReplyPacket(ByteString avatarEntityId, int areaId, Dictionary<string, object> stagesInfo) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("get_area_stages_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["ai"] = areaId,
            ["st"] = BsonHelper.ToBsonVal(stagesInfo),
        }.ToBson()),
    };
}

public sealed class AreaBuildReplyPacket(ByteString avatarEntityId, bool success, int areaId, Dictionary<string, object>? ext = null) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("area_build_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["rs"] = success,
            ["aid"] = areaId,
            ["ext"] = BsonHelper.ToBsonVal(ext ?? new Dictionary<string, object>()),
        }.ToBson()),
    };
}

public sealed class AreaLevelUpReplyPacket(ByteString avatarEntityId, bool success, int areaId, Dictionary<string, object>? ext = null) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("area_level_up_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["rs"] = success,
            ["aid"] = areaId,
            ["ext"] = BsonHelper.ToBsonVal(ext ?? new Dictionary<string, object>()),
        }.ToBson()),
    };
}

public sealed class EnterPatrolReplyPacket(ByteString avatarEntityId, bool success, bool rs, Dictionary<string, object>? ext = null) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("enter_patrol_reply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["s"] = success,
            ["rs"] = rs,
            ["ext"] = BsonHelper.ToBsonVal(ext ?? new Dictionary<string, object>()),
            ["ev"] = new BsonArray(),
        }.ToBson()),
    };
}

public sealed class NotifyAddFriendlyPacket(ByteString avatarEntityId, Dictionary<int, int> friendlyChanges) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage()
    {
        BsonDocument frDoc = new();
        foreach (KeyValuePair<int, int> kvp in friendlyChanges)
        {
            frDoc[kvp.Key.ToString()] = kvp.Value;
        }

        return new EntityMessage
        {
            Id = avatarEntityId,
            Method = AeadTool.EncodeMethodName("notify_add_friendly"),
            Parameters = ByteString.CopyFrom(new BsonDocument
            {
                ["fr"] = frDoc,
                ["n"] = new BsonDocument(),
            }.ToBson()),
        };
    }
}

public sealed class ZhaiReplyPacket(ByteString avatarEntityId, bool success, Dictionary<string, object>? reward = null) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("zhaiReply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["rs"] = success,
            ["reward"] = BsonHelper.ToBsonVal(reward ?? new Dictionary<string, object>()),
        }.ToBson()),
    };
}

public sealed class TeamOnLoginReplyPacket(ByteString avatarEntityId) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("teamOnLoginReply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["d"] = new BsonDocument(),
            ["t"] = 0,
        }.ToBson()),
    };
}

public sealed class LimitTeamOnLoginReplyPacket(ByteString avatarEntityId) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("limitTeamOnLoginReply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["d"] = new BsonDocument(),
            ["t"] = 0,
        }.ToBson()),
    };
}

public sealed class MultiTeamOnLoginReplyPacket(ByteString avatarEntityId) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("multiTeamOnLoginReply"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["d"] = new BsonDocument(),
            ["t"] = 0,
        }.ToBson()),
    };
}
