namespace Sv.Resources.Tables;

public sealed class KyusyoStoryData : TableToolsTableBase
{
    public const string TablePath = "Data/KyusyoStoryData";
    /// <summary>九霄故事同步包（SediStorySync 9228 等）单个通知携带的故事 ID 上限。</summary>
    public const int MaximumSyncStoryCount = 1000;

    public int StoryID { get; set; }
    public int Type { get; set; }
    public int UnlockLevelID { get; set; }
    public int StoryText { get; set; }
    public bool Repeatable { get; set; }
    public int ChoiceID { get; set; }

    public override int GetId() => GetDataItemIndex();

    public override void Verification()
    {
        if (StoryID <= 0 || Type < 0 || StoryText < 0)
        {
            throw new InvalidDataException($"KyusyoStoryData 存在非法 StoryID={StoryID}、Type={Type} 或 StoryText={StoryText}");
        }

        if (ChoiceID > 0 && GameTableCatalog.Instance.GetDataById<KyusyoStoryChoiceData>(ChoiceID) is null)
        {
            throw new InvalidDataException($"KyusyoStoryData {StoryID} 引用不存在的 ChoiceID {ChoiceID}");
        }

        // 哨兵行执行一次表级校验：故事 ID 去重数量不得超过客户端单包上限。
        IReadOnlyList<KyusyoStoryData> rows = GameTableCatalog.Instance.GetAllData<KyusyoStoryData>();
        if (ReferenceEquals(rows[0], this) && rows.Select(value => value.StoryID).Distinct().Count() > MaximumSyncStoryCount)
        {
            throw new InvalidDataException($"九霄故事同步超过客户端单包上限 {MaximumSyncStoryCount}，共 {rows.Select(value => value.StoryID).Distinct().Count()} 个");
        }
    }
}

public sealed class KyusyoStoryChoiceData : TableToolsTableBase
{
    public const string TablePath = "Data/KyusyoStoryChoiceData";

    public int ChoiceID { get; set; }
    public int ChoiceTitle { get; set; }
    public int Choice1Text { get; set; }
    public int Choice2Text { get; set; }
    public int Choice1Story { get; set; }
    public int Choice2Story { get; set; }
    public int MergedStory { get; set; }

    public override int GetId() => ChoiceID;

    public override void Verification()
    {
        foreach (int storyId in new[] { Choice1Story, Choice2Story, MergedStory }.Where(value => value > 0))
        {
            if (GameTableCatalog.Instance.GetAllData<KyusyoStoryData>().All(value => value.StoryID != storyId))
            {
                throw new InvalidDataException($"KyusyoStoryChoiceData {ChoiceID} 引用不存在的 StoryID {storyId}");
            }
        }
    }
}

public sealed class DlcStoryData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DLCStoryData";
    /// <summary>逐火 DLC 故事同步包（ZeroStorySync 5664）单个通知携带的故事 ID 上限。</summary>
    public const int MaximumSyncStoryCount = 1000;

    public int StoryID { get; set; }
    public int Type { get; set; }
    public int UnlockLevelID { get; set; }
    public int StoryText { get; set; }
    public bool Repeatable { get; set; }
    public int ChoiceID { get; set; }

    public override int GetId() => GetDataItemIndex();

    public override void Verification()
    {
        if (StoryID <= 0 || Type < 0 || StoryText < 0)
        {
            throw new InvalidDataException($"DlcStoryData 存在非法 StoryID={StoryID}、Type={Type} 或 StoryText={StoryText}");
        }

        if (ChoiceID > 0 && GameTableCatalog.Instance.GetDataById<DlcStoryChoiceData>(ChoiceID) is null)
        {
            throw new InvalidDataException($"DlcStoryData {StoryID} 引用不存在的 ChoiceID {ChoiceID}");
        }

        // 哨兵行执行一次表级校验：故事 ID 去重数量不得超过客户端单包上限。
        IReadOnlyList<DlcStoryData> rows = GameTableCatalog.Instance.GetAllData<DlcStoryData>();
        if (ReferenceEquals(rows[0], this) && rows.Select(value => value.StoryID).Distinct().Count() > MaximumSyncStoryCount)
        {
            throw new InvalidDataException($"逐火 DLC 故事同步超过客户端单包上限 {MaximumSyncStoryCount}，共 {rows.Select(value => value.StoryID).Distinct().Count()} 个");
        }
    }
}

public sealed class DlcStoryChoiceData : TableToolsTableBase
{
    public const string TablePath = "Data/dlc2/DLCStoryChoiceData";

    public int ChoiceID { get; set; }
    public int ChoiceTitle { get; set; }
    public int Choice1Text { get; set; }
    public int Choice2Text { get; set; }
    public int Choice1Story { get; set; }
    public int Choice2Story { get; set; }
    public int MergedStory { get; set; }

    public override int GetId() => ChoiceID;

    public override void Verification()
    {
        foreach (int storyId in new[] { Choice1Story, Choice2Story, MergedStory }.Where(value => value > 0))
        {
            if (GameTableCatalog.Instance.GetAllData<DlcStoryData>().All(value => value.StoryID != storyId))
            {
                throw new InvalidDataException($"DlcStoryChoiceData {ChoiceID} 引用不存在的 StoryID {storyId}");
            }
        }
    }
}
