namespace Sv.Resources.Tables;

public sealed class PartnerPosterData : TableToolsTableBase
{
    public const string TablePath = "Data/PartnerPosterData";

    public int PosterID { get; set; }
    public int FigureID { get; set; }
    public int Type { get; set; }
    public int PersonatedMaxIntimacy { get; set; }
    public int PosterName { get; set; }
    public int Live2D { get; set; }
    public int Group { get; set; }
    public int GroupName { get; set; }
    public int IsOpen { get; set; }
    public int DialogueX { get; set; }
    public int DialogueY { get; set; }
    public int IsPDAS { get; set; }
    public int IsEntry { get; set; }
    public int PartsNum { get; set; }

    public override int GetId() => PosterID;

    public bool IsGrantable => PosterID > 0 && IsOpen == 1;

    /// <summary>全部可开放看板（PosterID 为正且 IsOpen=1），按 PosterID 升序。</summary>
    public static IReadOnlyList<PartnerPosterData> GetGrantable() =>
        GameTableCatalog.Instance.GetAllData<PartnerPosterData>().Where(value => value.IsGrantable).OrderBy(value => value.PosterID).ToArray();

    public override void Verification()
    {
        if (PosterID <= 0 || PersonatedMaxIntimacy < 0 || PartsNum < 0)
        {
            throw new InvalidDataException($"看板 {PosterID} 配置非法");
        }

        // 哨兵行执行一次表级校验：必须存在可开放（IsOpen=1）看板，否则 giveall partner 无物可发。
        if (object.ReferenceEquals(GameTableCatalog.Instance.GetAllData<PartnerPosterData>()[0], this) && GameTableCatalog.Instance.GetAllData<PartnerPosterData>().All(value => !value.IsGrantable))
        {
            throw new InvalidDataException("PartnerPosterData 缺少可开放看板");
        }
    }
}

public sealed class PartnerPosterChangeData : TableToolsTableBase
{
    public const string TablePath = "Data/PartnerPosterChangeData";

    public int PosterID { get; set; }
    public int FigureID { get; set; }
    public List<int> ShowTime { get; set; } = [];
    public List<int> StartDate { get; set; } = [];
    public List<int> EndDate { get; set; } = [];

    public override int GetId() => checked(PosterID * 100000 + FigureID);
}

public sealed class PartnerStoryHeadData : TableToolsTableBase
{
    public const string TablePath = "Data/PartnerStoryHeadData";

    public int StroyID { get; set; }
    public int BackgroundID { get; set; }
    public int TitleText { get; set; }
    public int DialogueID { get; set; }
    public int Poster1 { get; set; }
    public int IntimacyRequire { get; set; }
    public int Awardcrystal { get; set; }
    public int Awardecho { get; set; }

    public override int GetId() => StroyID;

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<PartnerPosterData>(Poster1) is null || IntimacyRequire < 0 || Awardcrystal < 0 || Awardecho < 0)
        {
            throw new InvalidDataException($"看板故事 {StroyID} 配置非法");
        }
    }
}

public sealed class PartnerOffsetData : TableToolsTableBase
{
    public const string TablePath = "Data/PartnerOffsetData";

    public int PartnerOffsetID { get; set; }
    public int PosterID { get; set; }
    public float PosterScale { get; set; }
    public float PosterOffsetX { get; set; }
    public float PosterOffsetY { get; set; }
    public int PosterOffsetType { get; set; }
    public float Scale { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }

    public override int GetId() => PartnerOffsetID;
}

public sealed class RPStoryRewardData : TableToolsTableBase
{
    public const string TablePath = "Data/RPStoryRewardData";

    public int CharacterID { get; set; }
    public int TaskID { get; set; }
    public List<int> RewardID { get; set; } = [];
    public List<int> StoryID { get; set; } = [];

    public static int MakeId(int characterId, int taskId) => checked(characterId * 100000 + taskId);

    public override int GetId() => MakeId(CharacterID, TaskID);

    public override void Verification()
    {
        if (GameTableCatalog.Instance.GetDataById<RPData>(CharacterID) is null || GameTableCatalog.Instance.GetDataById<TaskData>(TaskID) is not { Type: 12, SubType: 1032 } || RewardID.Any(value => value > 0 && GameTableCatalog.Instance.GetDataById<RewardItemData>(value) is null))
        {
            throw new InvalidDataException($"RP 剧情任务 {CharacterID}:{TaskID} 配置非法");
        }
    }
}

public sealed class TaskData : TableToolsTableBase
{
    public const string TablePath = "Data/TaskData";

    public int ID { get; set; }
    public int Type { get; set; }
    public int SubType { get; set; }
    public int CumulateType { get; set; }
    public int Progress { get; set; }
    public int ParentID { get; set; }
    public List<string> Prop1 { get; set; } = [];
    public List<string> Prop2 { get; set; } = [];
    public List<string> Prop3 { get; set; } = [];
    public List<string> Prop4 { get; set; } = [];
    public List<string> Prop5 { get; set; } = [];
    public List<string> Prop6 { get; set; } = [];
    public List<string> Prop7 { get; set; } = [];
    public int Active { get; set; }
    public int AutoAccept { get; set; }

    public override int GetId() => ID;
}
