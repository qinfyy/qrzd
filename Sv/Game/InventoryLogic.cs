using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;
using MongoDB.Bson;

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

    public object[] ToSnapshot() => Comp.Items.Select(item => (object)new Dictionary<string, object>
    {
        ["u"] = new Dictionary<string, string> { ["$oid"] = item.Uuid }, ["i"] = item.ItemId, ["w"] = item.Count,
    }).ToArray();

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
        return [.. changed];
    }
}
