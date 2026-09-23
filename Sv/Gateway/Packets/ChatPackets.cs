using Google.Protobuf;
using Mobile.Server;
using MongoDB.Bson;
using Sv.Database;
using Sv.Game;
using Sv.Gateway.Protocol;

namespace Sv.Gateway.Packets;

public sealed class SyncChatMsgPacket(ByteString avatarEntityId, int channel, int subChannel, BsonDocument content) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName("syncMsg"),
        Parameters = ByteString.CopyFrom(new BsonDocument
        {
            ["cn"] = channel,
            ["scn"] = subChannel,
            ["cts"] = new BsonArray { content },
        }.ToBson()),
    };
}

public sealed class GameMasterStatePacket(ByteString avatarEntityId, string methodName, BsonDocument args) : BasePacket
{
    public override ushort Method => 5;

    public override IMessage CreateMessage() => new EntityMessage
    {
        Id = avatarEntityId,
        Method = AeadTool.EncodeMethodName(methodName),
        Parameters = ByteString.CopyFrom(args.ToBson()),
    };

    public static GameMasterStatePacket Money(ByteString id, int value) => new(id, "updateMoney", new BsonDocument("v", value));

    public static GameMasterStatePacket Crystal(ByteString id, int value) =>
        new(id, "updateCrystal", new BsonDocument { ["v"] = value, ["bv"] = 0 });

    public static GameMasterStatePacket Items(ByteString id, IReadOnlyList<ItemState> items)
    {
        BsonArray updates = [];
        foreach (ItemState item in items)
        {
            updates.Add(new BsonDocument { ["u"] = ObjectId.Parse(item.Uuid), ["i"] = item.ItemId, ["w"] = item.Count });
        }
        return new GameMasterStatePacket(id, "updateItems", new BsonDocument("i", updates));
    }

    public static GameMasterStatePacket City(ByteString id, Player player) => new(id, "get_city_data_reply", new BsonDocument
    {
        ["cd"] = new BsonDocument { ["av"] = player.City.ActionVal },
    });

    public static GameMasterStatePacket Week(ByteString id, Player player) => new(id, "syncWeeknumData", new BsonDocument
    {
        ["d"] = new BsonDocument
        {
            ["w"] = player.WeekNum.Week,
            ["d"] = player.WeekNum.Day,
            ["c"] = new BsonDocument(),
            ["l"] = new BsonDocument(),
        },
    });

    public static GameMasterStatePacket Hero(ByteString id, Player player, int heroId)
    {
        object[] row = player.HeroMgr.ToSnapshot().Cast<object[]>().First(value => (int)value[0] == heroId);
        return new GameMasterStatePacket(id, "addHero", new BsonDocument
        {
            ["h"] = heroId,
            ["d"] = BsonTypeMapper.MapToBsonValue(row[1]),
        });
    }

}
