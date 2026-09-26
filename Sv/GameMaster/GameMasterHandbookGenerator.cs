using System.Text;
using Serilog;
using Sv.Configuration;
using Sv.Game;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.GameMaster;

public static class GameMasterHandbookGenerator
{
    private static readonly ILogger Logger = Log.ForContext(typeof(GameMasterHandbookGenerator));

    public static void Generate(string contentRootPath)
    {
        string outputPath = Path.Combine(contentRootPath, GameMasterOptions.HandbookRelativePath);
        File.WriteAllText(outputPath, BuildContent(), new UTF8Encoding(false));
        Logger.Information("QRZD GM Handbook 已生成: {Path}", outputPath);
    }

    public static string BuildContent()
    {
        StringBuilder builder = new();
        builder.AppendLine("QRZD GM Handbook");
        builder.AppendLine("================");
        builder.AppendLine("游戏内打开终端好友，私聊 Server 发送 help 或命令（可省略 /）；默认作用于自己。");
        builder.AppendLine("其他聊天频道支持 /命令 与 !命令，命令与结果仅回显到 Server 私聊，不广播。");
        builder.AppendLine("HTTP: GET /api/gm?content=/命令，目标通过 @uid 指定。");
        builder.AppendLine("HTTP 认证：GameMaster:ApiKey 为空时仅允许回环地址；配置后使用 Authorization: Bearer <key>。");
        builder.AppendLine();

        foreach (GameMasterCommand command in GameMasterCommandRegistry.Commands)
        {
            builder.Append(command.Label);
            if (command.Aliases.Count > 0) builder.Append("（别名 ").Append(string.Join('、', command.Aliases)).Append('）');
            builder.Append("：").AppendLine(command.Description);
            foreach (string usage in command.Usage) builder.Append("    ").AppendLine(usage);
            foreach (string note in command.Notes) builder.Append("  - ").AppendLine(note);
            builder.AppendLine();
        }

        AppendInventoryCatalog(builder);
        return builder.ToString();
    }

    private static void AppendInventoryCatalog(StringBuilder builder)
    {
        builder.AppendLine("[客户端物品分类]");
        builder.AppendLine("来源: com.const.ItemType、entity/avatar_attrs/InvData.py、com/data/cdata/bag_main_show.py。");
        builder.AppendLine("逻辑分类与 UI 分页是两套字段：type 决定数据归属，ui_class 决定背包显示位置。");
        builder.AppendLine("all 仅包含 treasure、furniture、xinwu，按物品 ID 补齐；普通材料、任务物品、礼物、虚拟资产不隐式发放。");
        builder.AppendLine("ui1=贵重物品, ui2=兑换货币, ui3=活动道具, ui4=强化材料, ui5=影装材料, ui6=房间道具, ui7=染色材料, ui12=其它。");
        builder.AppendLine("common 是客户端 CATEGORY_COMMON 的真实背包物品子集，不代表所有物品；fragment 是 type=3/22 的神器使碎片。");
        builder.AppendLine("type=8 为虚拟资产，virtual_sub_type 决定钱包、皮肤、挂饰、徽章、称号等独立系统，不能用 updateItems 发放。");
        builder.AppendLine("GM 不扩容客户端背包；大量物品可能触发客户端背包满判定。影装之匣 155 缺少 hero_treasure 配置，不按普通影装发放。");
        builder.AppendLine();

        builder.AppendLine("[currency]");
        foreach (int id in PlayerProfileLogic.CurrencyItemIds)
        {
            ItemData item = GameTableCatalog.Instance.GetDataById<ItemData>(id)!;
            builder.Append(id).Append('\t').Append(item.Name).Append("\tGM=/give currency ").Append(id).AppendLine(" x100 [@uid]");
        }
        builder.AppendLine("晶钻 80 是 yuanbao，当前未实现；crystal/89 是晶尘，不能混用。");
        builder.AppendLine();

        // 神器使不进入背包，按 heroId 唯一持有，因此单列一节。
        // 异界体标记决定星级上限（5/4 而非 4/4），是使用这份表时唯一需要区分的属性。
        HeroData[] heroes = [.. GameTableCatalog.Instance.GetAllData<HeroData>().OrderBy(hero => hero.ProtoId)];
        builder.Append("[hero] 神器使 ID 数量: ").AppendLine(heroes.Length.ToString());
        builder.AppendLine("id\tname\t异界体\tGM");
        foreach (HeroData hero in heroes)
        {
            builder.Append(hero.ProtoId).Append('\t').Append(hero.Name)
                .Append('\t').Append(HeroMgrLogic.IsYijieti(hero.ProtoId) ? "是" : "否")
                .Append("\tGM=/give hero ").Append(hero.ProtoId).AppendLine(" [@uid]");
        }
        builder.AppendLine();
        builder.AppendLine("异界体星级上限 5/4，普通角色 4/4。养成直设用 /hero <id> [sl|so|art|aw|lib]。");
        builder.AppendLine();

        HashSet<int> listed = [];
        foreach (string category in new[] { "treasure", "furniture", "xinwu", "common", "task", "gift" })
        {
            ItemData[] items = InventoryLogic.GetGrantableItems(category);
            builder.Append('[').Append(category).Append("] 可发放 ID 数量: ").AppendLine(items.Length.ToString());
            foreach (ItemData item in items)
            {
                listed.Add(item.Id);
                builder.Append(item.Id).Append('\t').Append(item.Name).Append("\ttype=").Append(item.Type).Append('(').Append(item.TypeName)
                    .Append(")\tui_class=").Append(item.UiClass).Append("\twrap=").Append(item.Wrap).Append("\tGM=/give ").Append(item.Id).AppendLine(" [x数量] [@uid]");
            }
            builder.AppendLine();
        }

        builder.AppendLine("[未支持的非虚拟物品]");
        foreach (ItemData item in GameTableCatalog.Instance.GetAllData<ItemData>().Where(item => item.Release && item.Type != 8 && !listed.Contains(item.Id)).OrderBy(item => item.Id))
            builder.Append(item.Id).Append('\t').Append(item.Name).Append("\ttype=").Append(item.Type).AppendLine("\t缺少客户端分类或完整影装配置，不参与批量发放");
    }

}
