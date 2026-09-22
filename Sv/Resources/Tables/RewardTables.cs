using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

/// <summary>
/// 客户端 RewardItemDataLogic 使用的奖励定义。关卡表中的 ClearReward 字段引用本表 ID，
/// 不能直接当作装备或材料 MetaId。
/// </summary>
public sealed class RewardItemData : TableBase
{
    public const string TablePath = "Data/RewardItemData";

    public int ID { get; set; }

    public override int GetId() => ID;

    [Name("awardType")]
    public int AwardType { get; set; }

    [Name("num")]
    public int Num { get; set; }

    [Name("equipType")]
    public int EquipType { get; set; }

    [Name("equipID")]
    public int EquipID { get; set; }

    [Name("equipLevel")]
    public int EquipLevel { get; set; }

    [Name("equipStar")]
    public int EquipStar { get; set; }

    [Name("equipSkill")]
    public int EquipSkill { get; set; }

    [Name("hcoin")]
    public int HCoin { get; set; }

    [Name("name")]
    public string Name { get; set; } = string.Empty;

    public List<int> RewardIDList { get; set; } = [];

    /// <summary>
    /// 递归展开奖励组（AwardType=65 的行按 RewardIDList 继续下钻，数量按父级 Num 放大），
    /// 展开同时校验类型组合的合法性。关卡首通奖励、章节任务奖励等发放路径共用本入口。
    /// </summary>
    public static void ExpandReward(int rewardId, int multiplier, HashSet<int> expansionPath, List<StoryRewardDefinition> destination)
    {
        if (rewardId == 0)
        {
            return;
        }

        RewardItemData reward = GameTableCatalog.Instance.GetDataById<RewardItemData>(rewardId) ?? throw new InvalidDataException($"RewardItemData 缺少奖励 {rewardId}");
        if (!expansionPath.Add(rewardId))
        {
            throw new InvalidDataException($"RewardItemData 奖励组存在循环引用 {rewardId}");
        }

        try
        {
            int number = checked(reward.Num * multiplier);
            if (reward.AwardType == 65)
            {
                foreach (int childId in reward.RewardIDList.Where(value => value != 0))
                {
                    ExpandReward(childId, number, expansionPath, destination);
                }
                return;
            }

            ValidateRewardCombination(reward, number);
            destination.Add(new StoryRewardDefinition(
                reward.ID,
                reward.AwardType,
                number,
                reward.EquipType,
                reward.EquipID,
                reward.EquipLevel,
                reward.EquipStar,
                reward.EquipSkill));
        }
        finally
        {
            expansionPath.Remove(rewardId);
        }
    }

    /// <summary>被发放路径引用的奖励行必须落在已支持的类型组合内；未被引用的行不做此约束。</summary>
    private static void ValidateRewardCombination(RewardItemData reward, int number)
    {
        if (number <= 0)
        {
            throw new InvalidDataException($"奖励 {reward.ID} 的数量非法");
        }

        switch (reward.AwardType, reward.EquipType)
        {
            case (1, 10):
                if (reward.EquipID is <= 0 or > ushort.MaxValue || GameTableCatalog.Instance.GetDataById<StackMaterialData>(reward.EquipID) is not { MaxStock: > 0 })
                {
                    throw new InvalidDataException($"奖励 {reward.ID} 引用了不存在的材料 {reward.EquipID}");
                }
                break;
            case (1, 1):
            case (1, 2):
            case (1, 3):
            case (1, 5):
            case (1, 6):
                if (!EquipmentTableBase.Exists(checked((byte)reward.EquipType), reward.EquipID))
                {
                    throw new InvalidDataException($"奖励 {reward.ID} 引用了不存在的装备 {reward.EquipType}:{reward.EquipID}");
                }
                if (reward.EquipLevel is < 0 or > byte.MaxValue || reward.EquipStar is < 0 or > byte.MaxValue || reward.EquipSkill is < 0 or > byte.MaxValue)
                {
                    throw new InvalidDataException($"奖励 {reward.ID} 的装备字段超出 140 范围");
                }
                if (reward.EquipType is 1 or 2 or 3 &&
                    EquipmentTableBase.Find(checked((byte)reward.EquipType), reward.EquipID) is { } equipment &&
                    reward.EquipLevel > equipment.MaxLv)
                {
                    throw new InvalidDataException($"奖励 {reward.ID} 的装备等级超过资源上限");
                }
                break;
            case (31, 2):
            case (4, _):
                break;
            // 经验（2）与金币（5）：数值货币，入 PlayerBasicComp 经验/软币账户，与 4 水晶
            // 同款无资源引用可校验。崩坏学园篇章节任务奖励含金币行（如 6219 = 5/0），
            // 与 PrepareRewardPlan 的类型集保持一致，避免 GM 整章发奖时被校验层拦截。
            case (2, _):
            case (5, _):
                break;
            // 祈之共鸣（RewardItemData 691 等）：数值货币，与 RolePotential/Kyusyo 发放
            // 同账户（PlayerBasicComp.BhDust，钱包行 coin_type=5），无资源引用可校验。
            case (12, _):
                break;
            // 伙伴看板：equipType 为 PartnerPosterData 的 PosterID，AddPartner 要求 IsOpen=1。
            case (32, _):
                if (reward.EquipType is <= 0 or > ushort.MaxValue || GameTableCatalog.Instance.GetDataById<PartnerPosterData>(reward.EquipType) is not { IsOpen: 1 })
                {
                    throw new InvalidDataException($"奖励 {reward.ID} 引用了不可发放的看板 {reward.EquipType}");
                }
                break;
            // CG 图鉴：equipType 为 CGUnlockData 的 CGid，也是 240 通知的列表元素（short）。
            case (49, _):
                if (reward.EquipType is <= 0 or > short.MaxValue || GameTableCatalog.Instance.GetDataById<CGUnlockData>(reward.EquipType) is null)
                {
                    throw new InvalidDataException($"奖励 {reward.ID} 引用了不存在的 CG {reward.EquipType}");
                }
                break;
            default:
                throw new InvalidDataException(
                    $"奖励 {reward.ID} 的类型组合 {reward.AwardType}/{reward.EquipType} 尚未支持");
        }
    }
}

/// <summary>
/// 客户端 CGData 读取的 CG 图鉴表（Data/CGUnlockData）。RewardItemData awardType=49
/// 的 equipType 即本表 CGid；UnlockType=0 的行所有账号默认解锁（官服登录 240 Type=0
/// 全量下发与该集合一致），其余类型由剧情/成就/运营解锁。
/// </summary>
public sealed class CGUnlockData : TableToolsTableBase
{
    public const string TablePath = "Data/CGUnlockData";

    public int CGid { get; set; }

    public int Order { get; set; }

    public int Name { get; set; }

    public int UnlockType { get; set; }

    public int UnlockPara { get; set; }

    public override int GetId() => CGid;
}

/// <summary>
/// 客户端 RewardData 使用的货币展示定义。Type=30 的行同时是钱包同步所需的
/// 货币类型目录；商品弹窗会按 (Type, EquipType) 查找图标和本地化文本。
/// </summary>
public sealed class RewardData : TableToolsTableBase
{
    public const string TablePath = "Data/RewardData";

    public int Type { get; set; }

    public int EquipType { get; set; }

    public int TitleTextId { get; set; }

    public int DescTextId { get; set; }

    public string ImageWithBG { get; set; } = string.Empty;

    public string ImageWithoutBG { get; set; } = string.Empty;

    public int IsJumpOpen { get; set; }

    public int ExchangeTabText { get; set; }

    public int ExchangeType { get; set; }

    public override void Verification()
    {
        // 哨兵行执行一次表级校验：Type=30 的货币定义必须非空、按 EquipType 唯一
        // 且落在 Wallet CoinType 的 short 范围内（give/商店按它枚举可发放货币）。
        if (!ReferenceEquals(GameTableCatalog.Instance.GetAllData<RewardData>()[0], this))
        {
            return;
        }

        RewardData[] currencyDefinitions = GameTableCatalog.Instance.GetAllData<RewardData>()
            .Where(value => value.Type == 30 && value.EquipType > 0)
            .ToArray();
        if (currencyDefinitions.Length == 0 ||
            currencyDefinitions.Select(value => value.EquipType).Distinct().Count() != currencyDefinitions.Length ||
            currencyDefinitions.Any(value => value.EquipType > short.MaxValue))
        {
            throw new InvalidDataException("RewardData 商店货币定义为空、重复或超出 Wallet CoinType short 范围");
        }
    }
}
