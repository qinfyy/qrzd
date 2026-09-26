using MongoDB.Bson;
using Sv.Database;
using Sv.Game;
using Sv.Gateway;
using Sv.Gateway.Packets;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.GameMaster;

public static class InventoryCommands
{
    private static readonly string[] CategoryNotes =
    [
        "分类来自客户端 ItemType：common 普通物品、task 任务物品、treasure 影装、gift 礼物/自选碎片/宠物兑换物、furniture 家具、xinwu 信物、fragment 神器使碎片。",
        "背包分页：ui1 贵重物品、ui2 兑换货币、ui3 活动道具、ui4 强化材料、ui5 影装材料、ui6 房间道具、ui7 染色材料、ui12 其它。分页只筛选真实背包物品。",
        "currency 仅支持金币 102、晶尘 89、欧泊 90，增加真实余额；晶钻 80、皮肤、挂饰、徽章、称号等其他虚拟资产尚未实现，不会作为普通物品入包。",
        "lv<等级> 为 1..100，cl<品质> 为 1..5，只用于新发放的影装；默认均为 1，不修改已有影装。",
    ];

    public static GameMasterCommand Give { get; } = new(
        "give",
        ["g"],
        "按客户端物品 ID 发放指定物品或已支持的货币。",
        ["/give <id> [x数量] [lv等级] [cl品质] [@uid]", "/give <分类> <id> [x数量] [lv等级] [cl品质] [@uid]"],
        [
            "数量默认 1，也接受裸数字或 *数量；仅接受显式 ID，批量发放使用 giveall。旧 item 命令已更名。",
            "普通物品单次最多 100000 个且不超过 100 组；非堆叠物品与影装单次最多 100 件。影装每件生成独立 UUID。",
            .. CategoryNotes,
        ],
        ExecuteGive,
        RequireTarget: true);

    public static GameMasterCommand GiveAll { get; } = new(
        "giveall",
        ["ga"],
        "按客户端分类补齐收藏或批量增加道具。",
        ["/giveall [all|treasure|furniture|xinwu] [lv等级] [cl品质] [@uid]", "/giveall <common|task|gift|fragment|ui编号|currency> x数量 [@uid]"],
        [
            "分类省略按 all：仅补齐影装、家具、信物，每个未拥有的物品 ID 发 1 件；重复执行不重复发放，不接受数量。",
            "普通物品/分页/currency 分类必须指定数量，按数量累加；不会跳过已拥有的道具，也不会隐式发放货币。",
            "只遍历 release=true 的可发放资源；跳过虚拟资产、未识别类型和缺少影装配置的条目。单次数量不符合任一道具限制时整批失败。",
            "GM 允许超过客户端常规背包容量，批量材料可能导致普通奖励领取受限；建议按 ui 分类选择需要的物品。",
            .. CategoryNotes,
        ],
        ExecuteGiveAll,
        RequireTarget: true);

    private static void ExecuteGive(CommandContext ctx)
    {
        string first = ctx.GetArg(0)?.ToLowerInvariant() ?? throw new GameMasterCommandException("用法: /give [分类] <id> [x数量]");
        if (first == "all") throw new GameMasterCommandException("give 只接受显式 ID，批量补齐请使用 giveall");
        string? category = null;
        int idIndex = 0;
        if (!int.TryParse(first, out _))
        {
            RequireCategory(first);
            category = first;
            idIndex = 1;
        }
        int itemId = ctx.RequirePositiveInt(idIndex, "/give [分类] <id> [x数量]");
        ParseModifiers(ctx, idIndex + 1, out int? amount, out int? level, out int? treasureClass);
        int count = amount ?? 1;
        ItemData data = GameTableCatalog.Instance.GetDataById<ItemData>(itemId) ?? throw new GameMasterCommandException($"道具 ID {itemId} 不存在");
        bool currency = data.Type == 8 && PlayerProfileLogic.CurrencyItemIds.Contains(itemId);
        if (category == "currency" && !currency || category is not (null or "currency") && (!data.IsStoredInInventory || !InventoryLogic.MatchesCategory(data, category)))
            throw new GameMasterCommandException($"{itemId}（{data.Name}）不属于 {category} 的可发放物品；客户端类型为 {data.TypeName}({data.Type})");
        if (data.Type != 5 && (level is not null || treasureClass is not null)) throw new GameMasterCommandException("只有影装接受 lv 和 cl 参数");
        if (data.Type == 8 && !currency)
            throw new GameMasterCommandException($"{itemId}（{data.Name}）是虚拟资产，virtual_sub_type={data.VirtualSubType}，对应系统尚未实现，不能放入背包");

        ExecuteWithSync(ctx, target =>
        {
            if (currency)
            {
                target.Profile.GrantCurrency(itemId, count);
                return $"已增加 {data.Name}({itemId}) x{count}，余额 {target.Profile.GetCurrency(itemId)}";
            }
            target.Inventory.GrantItem(itemId, count, level ?? 1, treasureClass ?? 1);
            return $"已发放 {data.Name}({itemId}) x{count}，客户端分类 {data.TypeName}({data.Type})";
        });
    }

    private static void ExecuteGiveAll(CommandContext ctx)
    {
        string category = ctx.GetArg(0)?.ToLowerInvariant() ?? "all";
        int modifierIndex = 1;
        if (category.StartsWith("lv", StringComparison.Ordinal) || category.StartsWith("cl", StringComparison.Ordinal))
        {
            category = "all";
            modifierIndex = 0;
        }
        RequireCategory(category);
        ParseModifiers(ctx, modifierIndex, out int? count, out int? level, out int? treasureClass);
        if (category is not ("all" or "treasure") && (level is not null || treasureClass is not null))
            throw new GameMasterCommandException("只有 all 和 treasure 分类接受影装参数");
        if (category == "currency" && count is null) throw new GameMasterCommandException("giveall currency 必须指定数量，例如 x100");

        ExecuteWithSync(ctx, target =>
        {
            if (category == "currency")
            {
                target.Profile.GrantAllCurrencies(count!.Value);
                return $"已增加金币、晶尘、欧泊各 {count.Value}；不包含晶钻或活动货币";
            }
            ItemState[] items = target.Inventory.GrantAll(category, count, level ?? 1, treasureClass ?? 1);
            int kinds = items.Select(item => item.ItemId).Distinct().Count();
            if (InventoryLogic.IsCollectionCategory(category)) return $"已补齐 {category} 可发放收藏，新增 {kinds} 个物品 ID；已有物品未重复发放";
            return $"已向 {category} 的 {kinds} 种可发放道具各增加 {count}，更新 {items.Length} 个背包条目";
        });
    }

    private static void RequireCategory(string category)
    {
        if (category != "currency" && !InventoryLogic.GrantCategories.Contains(category))
            throw new GameMasterCommandException($"未知分类 {category}；支持 {string.Join(", ", InventoryLogic.GrantCategories)}, currency");
    }

    private static void ParseModifiers(CommandContext ctx, int start, out int? amount, out int? level, out int? treasureClass)
    {
        amount = level = treasureClass = null;
        foreach (string argument in ctx.Args.Skip(start))
        {
            string token = argument.ToLowerInvariant();
            if (token.StartsWith("lv", StringComparison.Ordinal)) SetValue(ref level, token[2..], "lv", 100);
            else if (token.StartsWith("cl", StringComparison.Ordinal)) SetValue(ref treasureClass, token[2..], "cl", 5);
            else if (token.StartsWith('x') || token.StartsWith('*')) SetValue(ref amount, token[1..], "数量", int.MaxValue);
            else if (int.TryParse(token, out _)) SetValue(ref amount, token, "数量", int.MaxValue);
            else throw new GameMasterCommandException($"无法识别参数 {argument}；使用 x数量、lv等级、cl品质");
        }

        static void SetValue(ref int? destination, string text, string name, int maximum)
        {
            if (destination is not null) throw new GameMasterCommandException($"{name} 参数不能重复");
            if (!int.TryParse(text, out int value) || value <= 0 || value > maximum)
                throw new GameMasterCommandException($"{name} 必须为 1..{maximum} 的整数");
            destination = value;
        }
    }

    private static void ExecuteWithSync(CommandContext ctx, Func<Player, string> grant)
    {
        Player target = ctx.GetTargetPlayer();
        GatewaySession? session = ctx.TargetSession;
        string message;
        lock (target.SyncRoot)
        {
            byte[] before = target.SaveToBlob();
            List<(string Method, BsonDocument Args)> updates = [];
            void Collect(string method, Dictionary<string, object> values) => updates.Add((method, BsonHelper.ToBsonVal(values).AsBsonDocument));
            target.Notification += Collect;
            try
            {
                message = grant(target);
                target.Save();
            }
            catch
            {
                target.Restore(before);
                throw;
            }
            finally { target.Notification -= Collect; }

            if (session?.AvatarEntityId is { } entityId)
                foreach (var update in updates) session.SendPack(new AvatarRpcPacket(entityId, update.Method, update.Args));
        }
        ctx.SendMessage(message);
    }
}
