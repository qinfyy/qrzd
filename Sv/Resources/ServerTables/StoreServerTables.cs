using CsvHelper.Configuration.Attributes;
using Sv.Gateway.Packets;
using Sv.Resources.Tables;

namespace Sv.Resources.ServerTables;

/// <summary>
/// 服务端商店商品。每个分类（CoinType+ExchangeType）下的商品来自官服商店抓包提取，
/// 作为权威策划表；RewardId 大于 0 时奖励按 RewardItemData 解析，等于 0 时使用
/// RewardType/EquipType 等内联字段（皮肤、抽卡券等无 RewardItemData 行的商品）。
/// </summary>
public sealed class StoreItemData : TableToolsTableBase
{
    public const string TablePath = "ServerData/StoreItem";

    public int CoinType { get; set; }

    public int ExchangeType { get; set; }

    public int StoreItemNo { get; set; }

    public int StoreItemOrder { get; set; }

    public int RewardId { get; set; }

    public int RewardType { get; set; }

    public int Num { get; set; }

    public int EquipType { get; set; }

    public int EquipId { get; set; }

    public int EquipLevel { get; set; }

    public int EquipStar { get; set; }

    public int EquipSkill { get; set; }

    public int Coins { get; set; }

    public int ExchangeLimit { get; set; }

    public int RefreshType { get; set; }

    public int TagId { get; set; }

    public DateTimeOffset? OpenTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public string DescInfo { get; set; } = string.Empty;

    [Ignore]
    public StoreCategory Category => new(CoinType, ExchangeType);

    /// <summary>商品价格（Coins 列，正数由 Verification 保证）。</summary>
    public uint Price => checked((uint)Coins);

    /// <summary>
    /// 商品的表级奖励（RewardId 大于 0 时为 RewardItemData 行，否则 null——使用本行内联字段）。
    /// 校验由 Verification 保证 RewardId 引用存在。
    /// </summary>
    public RewardItemData? GetReward() =>
        RewardId > 0 ? GameTableCatalog.Instance.GetDataById<RewardItemData>(RewardId) : null;

    /// <summary>商品描述：DescInfo 为空时回退 RewardItemData 的资源名。</summary>
    public string GetDescription() =>
        DescInfo.Length > 0 ? DescInfo : RewardId > 0 ? GameTableCatalog.Instance.GetDataById<RewardItemData>(RewardId)?.Name ?? string.Empty : string.Empty;

    /// <summary>分类内商品（含兜底筛选），行序即官服抓包下发的商品顺序。</summary>
    public static IReadOnlyList<StoreItemData> GetCategoryItems(StoreCategory category) =>
        GameTableCatalog.Instance.GetAllData<StoreItemData>()
            .Where(value => value.CoinType == category.CoinType && value.ExchangeType == category.ExchangeType)
            .ToArray();

    public readonly record struct StoreCategory(int CoinType, int ExchangeType);

    public override void Verification()
    {
        if (Coins <= 0)
        {
            throw new InvalidDataException($"StoreItem ({CoinType},{ExchangeType}) 商品 {StoreItemNo} 价格必须为正数");
        }
        if (RewardId > 0 && GameTableCatalog.Instance.GetDataById<RewardItemData>(RewardId) is null)
        {
            throw new InvalidDataException(
                $"StoreItem ({CoinType},{ExchangeType}) 商品 {StoreItemNo} 引用了不存在的奖励 {RewardId}");
        }
        if (GameTableCatalog.Instance.GetAllData<StoreOfferData>().All(value => value.CoinType != CoinType || value.ExchangeType != ExchangeType))
        {
            throw new InvalidDataException($"StoreItem 分类 ({CoinType},{ExchangeType}) 缺少 StoreOffer 配置");
        }

        IReadOnlyList<StoreItemData> rows = GameTableCatalog.Instance.GetAllData<StoreItemData>();
        if (ReferenceEquals(rows[0], this))
        {
            foreach (IGrouping<StoreCategory, StoreItemData> group in rows.GroupBy(value => value.Category))
            {
                if (group.Select(value => value.StoreItemNo).Distinct().Count() != group.Count())
                {
                    throw new InvalidDataException($"StoreItem 分类 ({group.Key.CoinType},{group.Key.ExchangeType}) 存在重复商品编号");
                }
            }
        }
    }
}

/// <summary>
/// 旧商店（命令 72/265）的充值商品目录。ProductId 与官服抓包一致；
/// 购买需要真实支付渠道，私服只负责展示和返回列表。
/// </summary>
public sealed class StoreProductIdData : TableToolsTableBase
{
    public const string TablePath = "ServerData/StoreProductId";

    public string ProductId { get; set; } = string.Empty;

    public int IconsId { get; set; }

    public int NotBuyDespTextId { get; set; }

    public int AlreadyBuyDespTextId { get; set; }

    public int LeftDays { get; set; }

    public int BoxItemId { get; set; }

    public int TitleTextId { get; set; }

    public int LimitType { get; set; }

    public int CurPeriodRemainNum { get; set; }

    public int RealRemainNum { get; set; }

    public int MaxNum { get; set; }

    public int LeftTime { get; set; }

    public int GiftType { get; set; }

    public int Price { get; set; }

    public int BuyFlag { get; set; }

    public int IsFirstReward { get; set; }

    public int IconType { get; set; }
}
public sealed class StoreOfferData : TableBase
{
    public const string TablePath = "ServerData/StoreOffer";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int CoinType { get; set; }

    public int ExchangeType { get; set; }

    public int MaxItems { get; set; }

    public string AwardTypes { get; set; } = string.Empty;

    public string EquipTypes { get; set; } = string.Empty;

    public int PriceMultiplier { get; set; }

    public int ExchangeLimit { get; set; }

    public int RefreshType { get; set; }

    public int TagId { get; set; }

    /// <summary>
    /// 7971 的 store_max_page。该值由客户端 GeneralShopData 保存，不能按商品数推断。
    /// </summary>
    public int StoreMaxPage { get; set; }

    public IReadOnlySet<int> GetAwardTypes() => ParseSet(AwardTypes);

    public IReadOnlySet<int> GetEquipTypes() => ParseSet(EquipTypes);

    private static IReadOnlySet<int> ParseSet(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToHashSet();

    /// <summary>按分类查配置；分类不存在时返回 null。</summary>
    public static StoreOfferData? Find(int coinType, int exchangeType) =>
        GameTableCatalog.Instance.GetAllData<StoreOfferData>()
            .FirstOrDefault(value => value.CoinType == coinType && value.ExchangeType == exchangeType);

    /// <summary>按筛选条件从 RewardItemData 生成的兜底商品（价格 = hcoin 或 num × 倍率，升序取 MaxItems 条）。</summary>
    public IReadOnlyList<RewardItemData> SelectFilteredRewards()
    {
        HashSet<int> awardTypes = [.. GetAwardTypes()];
        HashSet<int> equipmentTypes = [.. GetEquipTypes()];
        return GameTableCatalog.Instance.GetAllData<RewardItemData>()
            .Where(reward => IsSupportedReward(reward, awardTypes, equipmentTypes))
            // 资源中的 hcoin 是显式价格；普通奖励没有该字段时，num 是该目录的价格/数量。
            .Where(reward => ResolvePrice(reward) > 0)
            .OrderBy(reward => reward.ID)
            .Take(MaxItems)
            .ToArray();
    }

    /// <summary>兜底商品价格：RewardItemData 的 hcoin 列（显式价格）或 num × 倍率；不可售返回 0。</summary>
    public uint ResolvePrice(RewardItemData reward)
    {
        int basePrice = reward.HCoin > 0 ? reward.HCoin : reward.Num;
        if (basePrice <= 0 || PriceMultiplier <= 0)
        {
            return 0;
        }

        return checked((uint)checked(basePrice * PriceMultiplier));
    }

    /// <summary>奖励是否落在本分类支持的类型组合内（与官服兜底目录的筛选口径一致）。</summary>
    private static bool IsSupportedReward(RewardItemData reward, IReadOnlySet<int> awardTypes, IReadOnlySet<int> equipmentTypes)
    {
        if (!awardTypes.Contains(reward.AwardType) || reward.Num <= 0)
        {
            return false;
        }

        return reward.AwardType switch
        {
            1 when reward.EquipType == 10 => equipmentTypes.Contains(10) && GameTableCatalog.Instance.GetDataById<StackMaterialData>(reward.EquipID) is not null,
            1 when reward.EquipType is 1 or 2 or 3 or 5 or 6 or 9 => equipmentTypes.Contains(reward.EquipType) && EquipmentTableBase.IsGrantable(checked((byte)reward.EquipType), reward.EquipID),
            4 => true,
            30 when reward.EquipType == (int)WalletCoinType.COMMON_AP => true,
            31 when reward.EquipType == 2 => true,
            37 when GameTableCatalog.Instance.GetAllData<TalentData>().Any(value => value.CharID == reward.EquipType) => true,
            _ => false,
        };
    }

    public override void Verification()
    {
        if (ID <= 0 || CoinType <= 0 || ExchangeType <= 0 || MaxItems is < 0 or > 100 || StoreMaxPage <= 0 || ExchangeLimit < 0 || RefreshType < 0 || TagId < 0)
        {
            throw new InvalidDataException($"StoreOffer {ID} 字段范围非法");
        }

        HashSet<int> awardTypes = [.. GetAwardTypes()];
        HashSet<int> equipmentTypes = [.. GetEquipTypes()];
        if (MaxItems == 0)
        {
            if (awardTypes.Count != 0 || equipmentTypes.Count != 0 || PriceMultiplier != 0)
            {
                throw new InvalidDataException($"StoreOffer {ID} 空分类不应包含商品筛选条件");
            }
        }
        else
        {
            if (PriceMultiplier <= 0)
            {
                throw new InvalidDataException($"StoreOffer {ID} 商品价格倍率必须为正数");
            }

            if (awardTypes.Count == 0 || equipmentTypes.Count == 0 || awardTypes.Any(value => value <= 0) || equipmentTypes.Any(value => value <= 0))
            {
                throw new InvalidDataException($"StoreOffer {ID} 奖励筛选条件为空或非法");
            }
        }

        // 哨兵行执行一次表级校验：分类（CoinType+ExchangeType）不允许重复定义。
        IReadOnlyList<StoreOfferData> offers = GameTableCatalog.Instance.GetAllData<StoreOfferData>();
        if (ReferenceEquals(offers[0], this) && offers.GroupBy(value => (value.CoinType, value.ExchangeType)).Any(value => value.Count() > 1))
        {
            throw new InvalidDataException("StoreOffer 重复定义商店分类");
        }
    }
}

/// <summary>
/// 新商店分类的页签标签配置。标签由官服 NewFetchStoreInfoRsp 提取为结构化策划数据，
/// 不把抓包包体或 Base64 放入服务端。
/// </summary>
public sealed class StoreTagData : TableBase
{
    public const string TablePath = "ServerData/StoreTag";

    public int ID { get; set; }

    public override int GetId() => ID;

    public int CoinType { get; set; }

    public int ExchangeType { get; set; }

    public int TagId { get; set; }

    public DateTimeOffset? BeginTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public int Order { get; set; }

    public int TextmapId { get; set; }

    /// <summary>分类的页签标签，按 Order、ID 升序。</summary>
    public static IReadOnlyList<StoreTagData> GetCategoryTags(int coinType, int exchangeType) =>
        GameTableCatalog.Instance.GetAllData<StoreTagData>()
            .Where(value => value.CoinType == coinType && value.ExchangeType == exchangeType)
            .OrderBy(value => value.Order)
            .ThenBy(value => value.ID)
            .ToArray();

    public override void Verification()
    {
        if (ID <= 0 || CoinType <= 0 || ExchangeType <= 0 || TagId <= 0 || Order < 0 || TextmapId < 0 || (EndTime is { } end && BeginTime is { } begin && begin > end))
        {
            throw new InvalidDataException($"StoreTag {ID} 字段范围非法");
        }
    }
}

/// <summary>
/// 新商店横幅。CoinType 为 0 时保留官服的主商店入口值；客户端只会在查询集合包含 0 时匹配，
/// 服务端不得将它扩写为所有货币类型。
/// </summary>
public sealed class StoreBannerData : TableBase
{
    public const string TablePath = "ServerData/StoreBanner";

    public int ID { get; set; }

    public override int GetId() => ID;

    public string BannerId { get; set; } = string.Empty;

    public int JumpType { get; set; }

    public int Order { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public string CoinType { get; set; } = string.Empty;

    public int IsWebImage { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public int ImageId { get; set; }

    public int HideLevel { get; set; }

    public int ShowType { get; set; }

    public int NoCoinHide { get; set; }

    /// <summary>横幅的货币入口列表，来自 CoinType 列的 "239|243" 形式。</summary>
    public IReadOnlyList<uint> GetCoinTypes() => CoinType
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => uint.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();

    /// <summary>全部横幅，按策划行 ID 升序（官服 484/7974 保持行顺序；Order 是客户端展示
    /// 排序字段，不能反过来作为服务端下发列表的排序键）。</summary>
    public static IReadOnlyList<StoreBannerData> GetAll() =>
        GameTableCatalog.Instance.GetAllData<StoreBannerData>().OrderBy(value => value.ID).ToArray();

    public override void Verification()
    {
        if (ID <= 0 || string.IsNullOrWhiteSpace(BannerId) ||
            JumpType < 0 || Order < 0 || string.IsNullOrWhiteSpace(CoinType) ||
            IsWebImage is < 0 or > 1 ||
            ImageId < 0 || HideLevel < 0 || ShowType < 0 || NoCoinHide < 0)
        {
            throw new InvalidDataException($"StoreBanner {ID} 字段范围非法");
        }

        if (EndTime is { } bannerEnd && StartTime is { } bannerStart && bannerStart > bannerEnd)
        {
            throw new InvalidDataException($"StoreBanner {ID} 的结束时间早于开始时间");
        }

        IReadOnlyList<uint> coinTypes = GetCoinTypes();
        if (coinTypes.Count == 0)
        {
            throw new InvalidDataException($"StoreBanner {ID} 没有货币入口");
        }
        if (coinTypes.Any(value => value > short.MaxValue) || JumpType > short.MaxValue || Order > short.MaxValue || ImageId < 0 || HideLevel > short.MaxValue)
        {
            throw new InvalidDataException($"StoreBanner {ID} 超出旧商店 484 字段范围");
        }

        // 官服横幅允许指向当前没有 7970 分类请求的入口（例如 76、243），
        // 且 coin_type=0 不是服务端通配符，因此不能用商品分类集合反向裁剪或扩写横幅。

        if (IsWebImage != 0 && string.IsNullOrWhiteSpace(ImageUrl))
        {
            throw new InvalidDataException($"StoreBanner {ID} 的网页图片缺少 URL");
        }

        // 哨兵行执行一次表级校验：横幅非空且 BannerId 唯一。
        IReadOnlyList<StoreBannerData> banners = GameTableCatalog.Instance.GetAllData<StoreBannerData>();
        if (ReferenceEquals(banners[0], this))
        {
            if (banners.Count == 0)
            {
                throw new InvalidDataException("StoreBanner 没有可同步的商店横幅");
            }
            if (banners.Select(value => value.BannerId).Distinct(StringComparer.Ordinal).Count() != banners.Count)
            {
                throw new InvalidDataException("StoreBanner 存在重复 BannerId");
            }
        }
    }
}
