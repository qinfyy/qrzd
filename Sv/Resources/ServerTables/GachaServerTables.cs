using Sv.Resources.Tables;

namespace Sv.Resources.ServerTables;
public sealed class GachaPoolData : TableToolsTableBase
{
    public const string TablePath = "ServerData/GachaPool";

    public int GachaType { get; set; }

    public int Priority { get; set; }

    public int ImageId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public string EquipIds { get; set; } = string.Empty;

    /// <summary>
    /// 卡池档期起止，对应官服 GachaPoolInfo.start_time / end_time；留空表示未设置，该池视作常驻。
    /// 官服**限时池的档期是会轮换的**：2026-08-16 抓包中池 2/3 为 08-12~08-23，
    /// 2026-09-15 抓包中同一批池已轮换为 08-23~09-20，故此处的值是某一期官服的快照，
    /// 并非永久配置。池 1 是常驻池，池 10 走 <see cref="DurationDays"/> 按账号起算。
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <inheritdoc cref="StartTime"/>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>倒计时展示截止时刻，对应官服 GachaPoolInfo.show_end_time，格式同 <see cref="StartTime"/>。</summary>
    public DateTimeOffset? ShowEndTime { get; set; }

    public string EquipUpIds { get; set; } = string.Empty;

    public string EquipChooseIds { get; set; } = string.Empty;

    /// <summary>
    /// 自选池的候选装备分组，组间以 <c>;</c> 分隔、组内以 <c>|</c> 分隔。
    /// 对应客户端 GachaPoolInfo.equip_choose_item_list，每组是玩家可整组自选的一套装备。
    /// </summary>
    public string EquipChooseItemList { get; set; } = string.Empty;

    public int BackgroundImageId { get; set; }

    public string BackgroundUrl { get; set; } = string.Empty;

    public int ButtonImageId { get; set; }

    public string ButtonUrl { get; set; } = string.Empty;

    public int JumpParameter { get; set; }

    public int DiscountPrice { get; set; }

    public int DiscountTimes { get; set; }

    public int RemainDiscountTimes { get; set; }

    public int ChangePriorityLevel { get; set; }

    public int TotalPrayLimit { get; set; }

    /// <summary>
    /// 该池优先使用的祈愿票类型（对应客户端 EggTicketType），0 表示不使用票券而直接扣货币。
    /// 票不足时回退到 GachaConfig 按 GachaClazz 配置的水晶 / 友情点消耗。
    /// </summary>
    public int TicketType { get; set; }

    /// <summary>每次祈愿消耗的票数。</summary>
    public int TicketCost { get; set; }

    /// <summary>
    /// 该池祈愿票对应的堆叠材料 ID（客户端 GachaTicket2StackMaterialIdMap：公主祈愿 9072 公主之星、
    /// 魔女祈愿 9073 魔女之星等）。玩家以材料形态获得票时按此回填钱包票券，0 表示该池无票材料。
    /// </summary>
    public int TicketMaterialId { get; set; }

    /// <summary>
    /// 保底阈值：距上次神器满这么多次祈愿时，下一抽必出神器。
    /// 取自官服概率公示（公主祈愿 20、魔女祈愿 14、萌星祈愿 10）；0 表示该池无保底。
    /// </summary>
    public int PityTimes { get; set; }

    /// <summary>
    /// 限时天数：大于 0 时该池是**按账号起算**的限时池，实际档期由玩家创建时间推导，
    /// 表里的 StartTime/EndTime 仅作兜底。萌星祈愿公示即为「共计 201 次、限时 30 天」。
    /// </summary>
    public int DurationDays { get; set; }

    public IReadOnlyList<uint> ParseEquipIds() => ParseUIntList(EquipIds);

    public IReadOnlyList<uint> ParseEquipUpIds() => ParseUIntList(EquipUpIds);

    public IReadOnlyList<uint> ParseEquipChooseIds() => ParseUIntList(EquipChooseIds);

    /// <summary>自选池的分组候选；组为空时该池不提供自选。</summary>
    public IReadOnlyList<IReadOnlyList<uint>> ParseEquipChooseItemList() => EquipChooseItemList
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(group => (IReadOnlyList<uint>)ParseUIntList(group))
        .ToArray();

    private static IReadOnlyList<uint> ParseUIntList(string value) => value
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(uint.Parse)
        .ToArray();
}

/// <summary>
/// 祈愿票材料与票型的对应关系。客户端对祈愿票同时存在「背包物品」与「票数」两种形态，
/// 且票型到材料的对应在客户端里是硬编码的（EggMachineGeneralLogic 静态构造的
/// GachaTicket2StackMaterialIdMap），策划表没有这张关系，故由服务端自行维护。
/// 同一票型可能对应多个材料 ID（如萌萌星的旧 9075 与新 9661）。
/// </summary>
public sealed class GachaTicketMaterialData : TableToolsTableBase
{
    public const string TablePath = "ServerData/GachaTicketMaterial";

    /// <summary>祈愿票的堆叠材料 ID。</summary>
    public int MaterialId { get; set; }

    /// <summary>票型，沿用客户端 TicketStatusSync 的编号。</summary>
    public int TicketType { get; set; }
}

/// <summary>
/// 祈愿票兑换商店（客户端 MoleMole.TicketStoreExchangeWritePacket，命令 526）。
/// 官服以钱包币种 TICKET(239) 计价：抓包中兑换 8 张公主之星使该币种由 4568 降到 2328，单价 280。
/// </summary>
public sealed class GachaTicketStoreData : TableToolsTableBase
{
    public const string TablePath = "ServerData/GachaTicketStore";

    /// <summary>票型，沿用客户端 TicketStatusSync 的编号。</summary>
    public int TicketType { get; set; }

    /// <summary>计价币种（钱包 coinType），官服为 239 TICKET。</summary>
    public int CoinType { get; set; }

    /// <summary>每张票的单价。</summary>
    public int UnitPrice { get; set; }
}

/// <summary>服务端自建抽卡消耗配置（按 GachaClazz）。</summary>
public sealed class GachaConfigTableData : TableToolsTableBase
{
    public const string TablePath = "ServerData/GachaConfig";

    public int GachaClazz { get; set; }

    public int HCoin { get; set; }

    public int Fpoint { get; set; }

    public int SCoinAward { get; set; }

    public int BhDustAward { get; set; }

    public int YwDustAward { get; set; }
}

/// <summary>
/// 抽卡掉落条目。权重单位为万分之一；同一 GachaType 下的全部条目构成该池的奖池。
/// <see cref="ItemType"/> 复用装备类型编号：1 武器 / 2 服装 / 3 徽章 / 5 角色 / 6 萌章 / 9 使魔碎片，
/// 另有两个非装备类型：0 表示堆叠材料（<see cref="MetaId"/> 为材料 ID），-1 表示角色皮肤。
/// </summary>
public sealed class GachaRuleData : TableToolsTableBase
{
    public const string TablePath = "ServerData/GachaRule";

    /// <summary>堆叠材料：MetaId 指向 StackMaterialData。</summary>
    public const int MaterialItemType = 0;

    /// <summary>角色皮肤：MetaId 指向 PlayerSkinData。</summary>
    public const int SkinItemType = -1;

    public int ID { get; set; }

    public int GachaType { get; set; }

    public int ItemType { get; set; }

    public int MetaId { get; set; }

    public int Count { get; set; }

    public int Level { get; set; }

    public int Star { get; set; }

    public int Skill { get; set; }

    /// <summary>UP 产出的标记，响应中体现为 is_god，客户端据此播放神器特效。</summary>
    public int Quality { get; set; }

    public int Weight { get; set; }
}
