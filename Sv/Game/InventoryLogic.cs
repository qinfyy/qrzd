using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;
using MongoDB.Bson;
using System.Text.Json;

namespace Sv.Game;

public sealed class InventoryLogic(Player player) : PlayerLogicBase(player)
{
    private InventoryComp Comp => Player.SaveData.InventoryComp;

    public int MaxCost
    {
        get => Comp.MaxCost;
        set
        {
            if (Comp.MaxCost != value)
            {
                Comp.MaxCost = value;
                MarkDirty();
            }
        }
    }

    public object[] ToSnapshot() => Comp.Items.Select(item => (object)ItemSnapshot(item, true)).ToArray();

    private static Dictionary<string, object> ItemSnapshot(ItemState item, bool snapshot = false) => new()
    {
        ["u"] = snapshot ? new Dictionary<string, string> { ["$oid"] = item.Uuid } : ObjectId.Parse(item.Uuid),
        ["i"] = item.ItemId, ["w"] = item.Count, ["tr"] = CombatLogic.ReadJson(item.TreasureJson),
    };

    public bool CanGrantTreasure(int id, int level, int count) => level is >= 1 and <= 100 && count is > 0 and <= 100 &&
        GameTableCatalog.Instance.GetDataById<ItemData>(id)?.Type == 5 && GameTableCatalog.Instance.GetDataById<HeroTreasureData>(id * 10000 + 1) is not null;

    public object[] GrantTreasure(int id, int level, int count)
    {
        if (!CanGrantTreasure(id, level, count)) throw new InvalidOperationException("影装配置无效");
        HeroTreasureData row = GameTableCatalog.Instance.GetDataById<HeroTreasureData>(id * 10000 + 1)!;
        List<object> changed = [];
        for (int i = 0; i < count; i++)
        {
            List<object> attrs = [];
            if (row.Get("secondAttrs").ValueKind == JsonValueKind.Object)
            {
                foreach (var attr in row.Get("secondAttrs").EnumerateObject())
                {
                    JsonElement[] range = MainlineTable.Elements(attr.Value);
                    double min = range[0].GetDouble();
                    double max = range[1].GetDouble();
                    attrs.Add(new Dictionary<string, object> { ["n"] = attr.Name, ["v"] = min + Random.Shared.NextDouble() * (max - min) });
                }
            }
            ItemState item = new()
            {
                Uuid = ObjectId.GenerateNewId().ToString(), ItemId = id, Count = 1,
                TreasureJson = JsonSerializer.Serialize(new Dictionary<string, object> { ["lv"] = level, ["cl"] = 1, ["attr"] = attrs }),
            };
            Comp.Items.Add(item);
            changed.Add(ItemSnapshot(item));
        }
        Notify("updateItems", new() { ["i"] = changed.ToArray() });
        MarkDirty();
        return changed.ToArray();
    }

    public void ResetStoryItems()
    {
        Comp.Items.Clear();
        MarkDirty();
    }

    public ItemState[] Grant(int itemId, int count)
    {
        if (count is < 1 or > 100000 || !GameTableCatalog.Instance.TryGetDataById<ItemData>(itemId, out ItemData? data))
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "道具 ID 不存在或数量无效");
        }

        if (data.Type == 0 || data.Type == 5 || data.Wrap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "该道具类型暂不支持发放");
        }
        if ((long)count > (long)data.Wrap * 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "单次最多发放 100 组道具");
        }

        List<ItemState> changed = [];
        while (count > 0)
        {
            ItemState? item = Comp.Items.FirstOrDefault(value => value.ItemId == itemId && value.Count < data.Wrap);
            if (item is null)
            {
                item = new ItemState { Uuid = ObjectId.GenerateNewId().ToString(), ItemId = itemId };
                Comp.Items.Add(item);
            }

            int added = Math.Min(count, data.Wrap - item.Count);
            item.Count += added;
            count -= added;
            changed.Add(item);
        }
        MarkDirty();
        Notify("updateItems", new() { ["i"] = changed.Select(item => (object)new Dictionary<string, object>
        {
            ["u"] = ObjectId.Parse(item.Uuid), ["i"] = item.ItemId, ["w"] = item.Count,
        }).ToArray() });
        return [.. changed];
    }

    public int Count(int itemId) => Comp.Items.Where(item => item.ItemId == itemId).Sum(item => item.Count);

    public Dictionary<int, int> Counts() => Comp.Items.GroupBy(item => item.ItemId).ToDictionary(group => group.Key, group => group.Sum(item => item.Count));

    public bool CanGrant(int itemId, int count)
    {
        return count is > 0 and <= 100000 && GameTableCatalog.Instance.TryGetDataById<ItemData>(itemId, out var data) &&
            data.Type is not (0 or 5) && data.Wrap > 0 && count <= (long)data.Wrap * 100;
    }

    public bool Consume(int itemId, int count)
    {
        if (count < 0 || Count(itemId) < count) return false;
        foreach (ItemState item in Comp.Items.Where(item => item.ItemId == itemId).ToArray())
        {
            int consumed = Math.Min(count, item.Count);
            item.Count -= consumed;
            count -= consumed;
            Notify("updateItems", new() { ["i"] = new object[] { new Dictionary<string, object>
            {
                ["u"] = ObjectId.Parse(item.Uuid), ["i"] = item.ItemId, ["w"] = item.Count,
            } } });
            if (item.Count == 0) Comp.Items.Remove(item);
            if (count == 0) break;
        }
        MarkDirty();
        return true;
    }
}
