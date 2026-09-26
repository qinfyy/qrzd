using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;
using MongoDB.Bson;
using System.Text.Json;

namespace Sv.Game;

public sealed class InventoryLogic(Player player) : PlayerLogicBase(player)
{
    public static IReadOnlyList<string> GrantCategories { get; } =
        ["all", "common", "task", "treasure", "gift", "furniture", "xinwu", "fragment", "ui1", "ui2", "ui3", "ui4", "ui5", "ui6", "ui7", "ui12"];

    // Client CombatAttrs.NAME_TO_INDEX and ItemData.loadTreasure: fixed attributes use fN, third attributes use tN.
    private static readonly Dictionary<string, int> TreasureAttributeIndices = new()
    {
        ["_phy_str_delta"] = 30, ["_mag_str_delta"] = 31, ["_phy_def_delta"] = 32, ["_mag_def_delta"] = 33, ["_hp_delta"] = 34,
        ["block_rate"] = 6, ["penetrate_rate"] = 7, ["bless_rate"] = 8, ["injure_rate"] = 9, ["attack_twice_rate"] = 10,
        ["avoid_rate"] = 11, ["cri_rate"] = 12, ["cd_reset_rate"] = 13, ["magic_shield_val"] = 14, ["delay_hurt_rate"] = 17,
    };

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
        ["ctt"] = (long)(uint)ObjectId.Parse(item.Uuid).Timestamp,
    };

    public static bool IsCollectionCategory(string category) => category is "all" or "treasure" or "furniture" or "xinwu";

    public static bool MatchesCategory(ItemData item, string category) => category switch
    {
        "all" => item.Type is 5 or 13 or 20,
        "fragment" => item.Type is 3 or 22,
        "ui1" => item.UiClass == 1, "ui2" => item.UiClass == 2, "ui3" => item.UiClass == 3, "ui4" => item.UiClass == 4,
        "ui5" => item.UiClass == 5, "ui6" => item.UiClass == 6, "ui7" => item.UiClass == 7, "ui12" => item.UiClass == 12,
        _ => item.InventoryCategory == category,
    };

    public static ItemData[] GetGrantableItems(string category, int treasureClass = 1)
    {
        if (!GrantCategories.Contains(category)) throw new ArgumentOutOfRangeException(nameof(category), "未知物品分类，请查看 /help giveall");
        return GameTableCatalog.Instance.GetAllData<ItemData>()
            .Where(item => item.Release && item.IsStoredInInventory && MatchesCategory(item, category))
            .Where(item => item.Type != 5 || FindTreasureData(item.Id, treasureClass) is not null)
            .OrderBy(item => item.Id)
            .ToArray();
    }

    private static HeroTreasureData? FindTreasureData(int id, int treasureClass)
    {
        if (treasureClass is < 1 or > 5 || GameTableCatalog.Instance.GetDataById<ItemData>(id)?.Type != 5) return null;
        HeroTreasureData? row = GameTableCatalog.Instance.GetDataById<HeroTreasureData>(checked(id * 10000 + treasureClass));
        if (row is null) return null;
        foreach (string field in new[] { "secondAttrs", "thridAttrs" })
        {
            JsonElement attributes = row.Get(field);
            if (attributes.ValueKind == JsonValueKind.Undefined) continue;
            if (attributes.ValueKind != JsonValueKind.Object) return null;
            foreach (JsonProperty attribute in attributes.EnumerateObject())
            {
                JsonElement[] range = MainlineTable.Elements(attribute.Value);
                if (!TreasureAttributeIndices.ContainsKey(attribute.Name) || range.Length != 2 || range.Any(value => value.ValueKind != JsonValueKind.Number)) return null;
                if (!range[0].TryGetDouble(out double min) || !range[1].TryGetDouble(out double max) || !double.IsFinite(min) || !double.IsFinite(max) || min > max) return null;
            }
        }
        return row;
    }

    public bool CanGrantTreasure(int id, int level, int count) => level is >= 1 and <= 100 && count is > 0 and <= 100 && FindTreasureData(id, 1) is not null;

    public object[] GrantTreasure(int id, int level, int count)
    {
        if (!CanGrantTreasure(id, level, count)) throw new InvalidOperationException("影装配置无效");
        return GrantItem(id, count, level).Select(item => (object)ItemSnapshot(item)).ToArray();
    }

    public ItemState[] GrantItem(int id, int count, int level = 1, int treasureClass = 1, bool sync = true)
    {
        ItemData data = GameTableCatalog.Instance.GetDataById<ItemData>(id) ?? throw new ArgumentOutOfRangeException(nameof(id), "道具 ID 不存在");
        if (data.Type != 5)
        {
            if (level != 1 || treasureClass != 1) throw new ArgumentOutOfRangeException(nameof(level), "只有影装接受 lv 和 cl 参数");
            return Grant(id, count, sync);
        }
        if (level is < 1 or > 100 || count is < 1 or > 100 || FindTreasureData(id, treasureClass) is not { } row)
            throw new ArgumentOutOfRangeException(nameof(id), "影装配置无效；lv 为 1..100，cl 为 1..5，数量为 1..100");
        if (Comp.Items.Where(item => item.ItemId == id).Sum(item => (long)item.Count) + count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count), "持有数量超过上限");

        List<ItemState> changed = [];
        for (int i = 0; i < count; i++)
        {
            List<object> attrs = [];
            foreach ((string field, string prefix) in new[] { ("secondAttrs", "f"), ("thridAttrs", "t") })
            {
                if (row.Get(field).ValueKind != JsonValueKind.Object) continue;
                foreach (JsonProperty attr in row.Get(field).EnumerateObject())
                {
                    JsonElement[] range = MainlineTable.Elements(attr.Value);
                    double min = range[0].GetDouble();
                    double max = range[1].GetDouble();
                    attrs.Add(new Dictionary<string, object> { ["n"] = prefix + TreasureAttributeIndices[attr.Name], ["v"] = min + Random.Shared.NextDouble() * (max - min) });
                }
            }
            ItemState item = new()
            {
                Uuid = ObjectId.GenerateNewId().ToString(), ItemId = id, Count = 1,
                TreasureJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["lv"] = level, ["cl"] = treasureClass, ["attr"] = attrs, ["ft"] = row.Ints("fixTrick"), ["cct"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                }),
            };
            Comp.Items.Add(item);
            changed.Add(item);
        }
        MarkDirty();
        if (sync) Synchronize(changed);
        return [.. changed];
    }

    public ItemState[] GrantAll(string category, int? count = null, int level = 1, int treasureClass = 1)
    {
        bool collection = IsCollectionCategory(category);
        if (collection && count is not null) throw new ArgumentOutOfRangeException(nameof(count), "收藏分类只补齐缺失 ID，不接受数量；重复发放请使用 give");
        if (!collection && count is null) throw new ArgumentOutOfRangeException(nameof(count), "普通物品分类必须显式指定数量，例如 x100");
        if (count is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(count), "单种道具数量必须为 1..100000");
        if (level is < 1 or > 100 || treasureClass is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(level), "lv 为 1..100，cl 为 1..5");
        if (category is not ("all" or "treasure") && (level != 1 || treasureClass != 1))
            throw new ArgumentOutOfRangeException(nameof(level), "只有 all 和 treasure 分类接受影装参数");

        HashSet<int> owned = Comp.Items.Where(item => item.Count > 0).Select(item => item.ItemId).ToHashSet();
        ItemData[] items = GetGrantableItems(category, treasureClass).Where(item => !collection || !owned.Contains(item.Id)).ToArray();
        int amount = count ?? 1;
        long estimatedSlots = 0;
        foreach (ItemData item in items)
        {
            if (item.Type != 5 && !CanGrant(item.Id, amount))
                throw new ArgumentOutOfRangeException(nameof(count), $"道具 {item.Id}（{item.Name}）数量无效或超过单次 100 组/持有数量上限，未发放任何物品");
            int wrap = Math.Max(item.Wrap, 1);
            estimatedSlots += ((long)amount + wrap - 1) / wrap;
        }
        if (estimatedSlots > 10000) throw new ArgumentOutOfRangeException(nameof(count), "单次批量最多涉及 10000 组物品，请缩小分类或数量");

        List<ItemState> changed = [];
        foreach (ItemData item in items)
        {
            int itemLevel = item.Type == 5 ? level : 1;
            int itemClass = item.Type == 5 ? treasureClass : 1;
            changed.AddRange(GrantItem(item.Id, amount, itemLevel, itemClass, sync: false));
        }
        if (changed.Count > 0) Synchronize(changed);
        return [.. changed];
    }

    private void Synchronize(IEnumerable<ItemState> items)
    {
        foreach (ItemState[] batch in items.Chunk(50))
            Notify("updateItems", new() { ["i"] = batch.Select(item => (object)ItemSnapshot(item)).ToArray() });
    }

    public void ResetStoryItems()
    {
        Comp.Items.Clear();
        MarkDirty();
    }

    public ItemState[] Grant(int itemId, int count, bool sync = true)
    {
        if (count is < 1 or > 100000 || !GameTableCatalog.Instance.TryGetDataById<ItemData>(itemId, out ItemData? data))
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "道具 ID 不存在或数量无效");
        }

        if (!data.IsStoredInInventory || data.Type == 5)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "该道具类型暂不支持发放");
        }
        int wrap = Math.Max(data.Wrap, 1);
        if ((long)count > (long)wrap * 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "单次最多发放 100 组道具");
        }
        if (Comp.Items.Where(item => item.ItemId == itemId).Sum(item => (long)item.Count) + count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count), "持有数量超过上限");

        List<ItemState> changed = [];
        while (count > 0)
        {
            ItemState? item = data.Wrap > 0 ? Comp.Items.FirstOrDefault(value => value.ItemId == itemId && value.Count < wrap) : null;
            if (item is null)
            {
                item = new ItemState { Uuid = ObjectId.GenerateNewId().ToString(), ItemId = itemId };
                Comp.Items.Add(item);
            }

            int added = Math.Min(count, wrap - item.Count);
            item.Count += added;
            count -= added;
            changed.Add(item);
        }
        MarkDirty();
        if (sync) Synchronize(changed);
        return [.. changed];
    }

    public int Count(int itemId) => Comp.Items.Where(item => item.ItemId == itemId).Sum(item => item.Count);

    public Dictionary<int, int> Counts() => Comp.Items.GroupBy(item => item.ItemId).ToDictionary(group => group.Key, group => group.Sum(item => item.Count));

    public bool CanGrant(int itemId, int count)
    {
        return count is > 0 and <= 100000 && GameTableCatalog.Instance.TryGetDataById<ItemData>(itemId, out var data) &&
            data.IsStoredInInventory && data.Type != 5 && count <= (long)Math.Max(data.Wrap, 1) * 100 &&
            Comp.Items.Where(item => item.ItemId == itemId).Sum(item => (long)item.Count) + count <= int.MaxValue;
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
