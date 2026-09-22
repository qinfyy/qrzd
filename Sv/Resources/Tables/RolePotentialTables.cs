using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

public sealed class RoleDuplicateData : TableToolsTableBase
{
    public const string TablePath = "Data/RoleDuplicateData";

    public int RoleID { get; set; }
    public int DuplicateNum { get; set; }
    public int SpecialAttributeID { get; set; }
    public int SkillType { get; set; }
    public int Name { get; set; }
    public int Description { get; set; }
    public float Para1 { get; set; }
    public float Para2 { get; set; }
    public float Para3 { get; set; }
    public float Para4 { get; set; }
    public float Para5 { get; set; }

    public override int GetId() => CreateKey(RoleID, DuplicateNum);

    public static int CreateKey(int roleId, int duplicateNum) => checked((roleId * 10) + duplicateNum);

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<RoleDataV2>(RoleID) is null || DuplicateNum is < 1 or > 4)
        {
            throw new InvalidDataException($"RoleDuplicateData {RoleID}:{DuplicateNum} 配置非法");
        }
    }
}

public sealed class RoleMissionData : TableToolsTableBase
{
    public const string TablePath = "Data/RoleMissionData";

    public int MissionID { get; set; }
    public int RefreshType { get; set; }
    public int RoleID { get; set; }
    public int MissionType { get; set; }
    public int ParentID { get; set; }
    public int RewardNum { get; set; }
    public int Description { get; set; }
    public int Progress { get; set; }
    public int Para1 { get; set; }
    public int Para2 { get; set; }
    public int Para3 { get; set; }

    public override int GetId() => MissionID;

    [Ignore]
    public IReadOnlyList<RoleMissionData> Children { get; private set; } = [];

    public override void OnFinalize()
    {
        Children = GameTableCatalog.Instance.GetAllData<RoleMissionData>().Where(value => value.ParentID == MissionID).OrderBy(value => value.MissionID).ToArray();
    }

    public override void Verification()
    {
        if (MissionID <= 0 || RefreshType is not (1 or 2) || Progress <= 0 || RewardNum < 0 || (RoleID != 0 && GameTableCatalog.Instance.GetDataById<RoleDataV2>(RoleID) is null))
        {
            throw new InvalidDataException($"RoleMissionData {MissionID} 配置非法");
        }

        if (ParentID != 0)
        {
            RoleMissionData? parent = GameTableCatalog.Instance.GetDataById<RoleMissionData>(ParentID);
            if (parent is null)
            {
                throw new InvalidDataException($"RoleMissionData {MissionID} 引用了不存在的父任务 {ParentID}");
            }
            if (parent.RoleID != RoleID)
            {
                throw new InvalidDataException($"RoleMissionData {MissionID} 的父任务 {ParentID} 不属于同一角色");
            }
        }
    }
}

public sealed class RoleMissionRewardData : TableToolsTableBase
{
    public const string TablePath = "Data/RoleMissionRewardData";

    public int RoleID { get; set; }
    public int Progress { get; set; }
    public int RewardList { get; set; }
    public int KeyReward { get; set; }

    public override int GetId() => CreateKey(RoleID, Progress);

    public static int CreateKey(int roleId, int progress) => checked((roleId * 1000) + progress);

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<RoleDataV2>(RoleID) is null || Progress <= 0 || GameTableCatalog.Instance.GetDataById<RewardItemData>(RewardList) is null)
        {
            throw new InvalidDataException($"RoleMissionRewardData {RoleID}:{Progress} 配置非法");
        }
    }
}
