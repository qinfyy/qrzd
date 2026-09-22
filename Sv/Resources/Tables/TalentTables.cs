using System.Globalization;
using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

public sealed class TalentData : TableToolsTableBase
{
    public const string TablePath = "Data/TalentData";

    public int TalentID { get; set; }
    public int CharID { get; set; }
    public int Attributeid { get; set; }
    public int DisplayTitle { get; set; }
    public int DisplayDescription { get; set; }
    public int TalentType { get; set; }
    public float CoordX { get; set; }
    public float CoordY { get; set; }
    public List<int> ParentTalentID { get; set; } = [];
    public int MaxLevel { get; set; }
    public float prop1 { get; set; }
    public float prop2 { get; set; }
    public float prop3 { get; set; }
    public float prop4 { get; set; }
    public float prop5 { get; set; }
    public float propadd1 { get; set; }
    public float propadd2 { get; set; }
    public float propadd3 { get; set; }
    public float propadd4 { get; set; }
    public float propadd5 { get; set; }
    public int UIType { get; set; }
    public int CanDisable { get; set; }
    public int ConsumptionType { get; set; }

    /// <summary>峰值圣痕节点（TalentType 9/10），要求标准圣痕全满后才能投入。</summary>
    public bool IsPeak => TalentType is 9 or 10;

    /// <summary>所属圣痕开关（TalentSwitchData）的 SwitchID；0 表示不属于任何开关的第二层节点。</summary>
    [Ignore]
    public int OwningSwitchId { get; private set; }

    public override int GetId() => TalentID;

    public override void OnFinalize()
    {
        OwningSwitchId = GameTableCatalog.Instance.GetAllData<TalentSwitchData>()
            .FirstOrDefault(value => value.TalentIds.Contains(TalentID))?.SwitchID ?? 0;
    }

    public override void Verification()
    {
        if (TalentID <= 0 || CharID <= 0 || MaxLevel <= 0 || ParentTalentID.Any(value => value < 0))
        {
            throw new InvalidDataException($"TalentData {TalentID} 配置非法");
        }

        foreach (int parent in ParentTalentID.Where(value => value > 0))
        {
            if (GameTableCatalog.Instance.GetDataById<TalentData>(parent) is not { CharID: var parentChar } || parentChar != CharID)
            {
                throw new InvalidDataException($"TalentData {TalentID} 缺少前置天赋 {parent}");
            }
        }

        // 哨兵行执行一次域级校验：PriceData 必须提供圣痕重置价格。
        if (ReferenceEquals(GameTableCatalog.Instance.GetAllData<TalentData>()[0], this))
        {
            int resetPrice = GetResetPrice();
            if (resetPrice <= 0)
            {
                throw new InvalidDataException("PriceData 缺少 branch_CN.TALENT_RESET_PRICE");
            }
        }
    }

    /// <summary>圣痕重置价格，来自客户端 PriceData JSON 的 branch_CN 分支。</summary>
    public static int GetResetPrice() =>
        GameTableCatalog.Instance.GetJson<Dictionary<string, PriceData>>()
            .GetValueOrDefault("branch_CN")?.TALENT_RESET_PRICE ?? 0;
}

public sealed class TalentConsumptionData : TableToolsTableBase
{
    public const string TablePath = "Data/TalentConsumptionData";
    /// <summary>通用角色（未逐角色配置消耗时）的 CharaID 占位值。</summary>
    public const int CommonCharaId = 9999;

    public int CharaID { get; set; }
    public int ConsumptionType { get; set; }
    public int Level { get; set; }
    public int Consumption { get; set; }

    public override int GetId() => CreateKey(CharaID, ConsumptionType, Level);

    public static int CreateKey(int roleId, int consumptionType, int level) =>
        checked((((roleId * 10) + consumptionType) * 1000) + level);

    /// <summary>按角色、消耗类型与等级取圣痕点数消耗；角色未配置时回退通用 CharaID=9999 行。</summary>
    public static TalentConsumptionData? GetConsumption(int roleId, int consumptionType, int level)
    {
        return GameTableCatalog.Instance.GetDataById<TalentConsumptionData>(CreateKey(roleId, consumptionType, level)) ??
               GameTableCatalog.Instance.GetDataById<TalentConsumptionData>(CreateKey(CommonCharaId, consumptionType, level));
    }
}

public sealed class TalentSwitchData : TableToolsTableBase
{
    public const string TablePath = "Data/TalentSwitchData";

    public int SwitchID { get; set; }
    public int CharID { get; set; }
    public int EnableLv { get; set; }
    public int MaxLevel { get; set; }
    public int SwitchCost { get; set; }
    public int UnlockCostID { get; set; }
    public int UnlockCostNum { get; set; }
    public int TalentID1 { get; set; }
    public int TalentID2 { get; set; }
    public int TalentID3 { get; set; }
    public int TalentID4 { get; set; }

    public override int GetId() => SwitchID;

    public IReadOnlyList<int> TalentIds =>
        new[] { TalentID1, TalentID2, TalentID3, TalentID4 }.Where(value => value > 0).ToArray();

    public override void Verification()
    {
        if (SwitchID <= 0 || CharID <= 0 || EnableLv <= 0 || MaxLevel <= 0 || SwitchCost < 0 || UnlockCostID <= 0 || UnlockCostNum <= 0)
        {
            throw new InvalidDataException($"TalentSwitchData {SwitchID} 配置非法");
        }

        foreach (int talentId in TalentIds)
        {
            if (GameTableCatalog.Instance.GetDataById<TalentData>(talentId) is not { CharID: var talentChar } || talentChar != CharID)
            {
                throw new InvalidDataException($"TalentSwitchData {SwitchID} 引用天赋 {talentId} 非法");
            }
        }
    }
}
