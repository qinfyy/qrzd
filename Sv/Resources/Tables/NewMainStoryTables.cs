using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

/// <summary>
/// 客户端 MoleMole.NewMainStoryMenuData 对应的 TSV 表项。
/// </summary>
public sealed class NewMainStoryMenuData : TableToolsTableBase
{
    public const string TablePath = "Data/NewMainStoryMenu";

    public int ID { get; set; }

    public int Type { get; set; }

    public int FatherID { get; set; }

    public int SeasonID { get; set; }

    public string StageName { get; set; } = string.Empty;

    public int MainNum { get; set; }

    public int Order { get; set; }

    public int FinishCondition { get; set; }

    public int UnlockCondition { get; set; }

    public string WebImageUrl { get; set; } = string.Empty;

    public int ChapterName { get; set; }

    public string PrefabName { get; set; } = string.Empty;

    public int BGType { get; set; }

    public int BGID { get; set; }

    public override int GetId() => ID;

    /// <summary>
    /// 本章节 stage 的派生关卡集合（SediStorySync 9228 的 levels 语义）：
    /// GeneralStageData[stage].levelTypeArray 匹配 LevelMetaV2.Type 且 Group != 0 的关卡。
    /// 官服 9228 每个 season 的 levels 恰为对应 stage 的该集合（崩坏学园篇 1-5 号抓包逐条
    /// 验证，含支线 PRX/BRX 与 RP 玩法全部一致）；Group=0 的行是孤立关卡，官服不带。
    /// </summary>
    [Ignore]
    public IReadOnlyList<int> StageLevelIds { get; private set; } = [];

    public override void OnFinalize()
    {
        if (SeasonID == 0)
        {
            return;
        }

        GeneralStageDataItem? stage = GameTableCatalog.Instance.GetJson<Dictionary<string, GeneralStageDataItem>>().GetValueOrDefault(StageName);
        if (stage is null || stage.GetLevelTypeList().Count == 0)
        {
            throw new InvalidDataException($"GeneralStageData 缺少崩坏学园篇章节 {StageName} 的 levelTypeArray");
        }

        HashSet<int> levelTypes = [.. stage.GetLevelTypeList()];
        StageLevelIds = GameTableCatalog.Instance.GetAllData<LevelMetaV2>().Where(value => value.Group != 0 && levelTypes.Contains(value.Type)).Select(value => value.ID).Order().ToArray();

        bool mainChapter = Type == 1;
        HashSet<int> chapterLayoutIds = mainChapter ? [.. GameTableCatalog.Instance.GetAllData<NewMainStoryChapterData>().Select(value => value.LevelID)] : [];
        foreach (int levelId in StageLevelIds)
        {
            LevelMetaV2 meta = GameTableCatalog.Instance.GetDataById<LevelMetaV2>(levelId) ?? throw new InvalidDataException($"崩坏学园篇关卡 {levelId} 不在 LevelMetaV2 中");
            meta.IsNewMainStory = true;
            meta.NewMainStoryStageName ??= StageName;
            if (mainChapter && !chapterLayoutIds.Contains(levelId))
            {
                meta.IsHiddenNewMainStoryLevel = true;
            }

            if (meta.FirstClearRewards.Count == 0)
            {
                List<StoryRewardDefinition> rewards = [];
                RewardItemData.ExpandReward(meta.ClearReward1, 1, [], rewards);
                RewardItemData.ExpandReward(meta.ClearReward2, 1, [], rewards);
                meta.FirstClearRewards = Array.AsReadOnly(rewards.ToArray());
            }
        }
    }

    public override void Verification()
    {
        if (!ReferenceEquals(GameTableCatalog.Instance.GetAllData<NewMainStoryMenuData>()[0], this))
        {
            return;
        }

        NewMainStoryMenuData storyCollection = GameTableCatalog.Instance.GetDataById<NewMainStoryMenuData>(1) ?? throw new InvalidDataException("NewMainStoryMenu 缺少 ID 1");
        if (storyCollection.Type != 2 || storyCollection.UnlockCondition != 0)
        {
            throw new InvalidDataException("NewMainStoryMenu 的初始剧情合集配置异常");
        }

        int[] trackedLevelIds = GameTableCatalog.Instance.GetAllData<NewMainStoryChapterData>()
            .Where(value =>value.StoryProgressList.Any(item => item != 0) || value.ClearProgressList.Any(item => item != 0))
            .Select(value => value.LevelID)
            .ToArray();
        if (!trackedLevelIds.SequenceEqual([601031, 601045]))
        {
            throw new InvalidDataException("NewMainStoryChapterData 的剧情进度关卡配置与客户端版本不匹配");
        }

        if (GameTableCatalog.Instance.GetDataById<RX2LevelData>(601063) is null)
        {
            throw new InvalidDataException("RX2LevelData 缺少首个章节节点 601063");
        }
    }
}

/// <summary>
/// 客户端 MoleMole.NewMainStoryChapterData 对应的 TSV 表项。
/// </summary>
public sealed class NewMainStoryChapterData : TableToolsTableBase
{
    public const string TablePath = "Data/NewMainStoryChapterData";

    public int LevelID { get; set; }

    public int ShowType { get; set; }

    public string LevelIcon { get; set; } = string.Empty;

    public string EffectPath { get; set; } = string.Empty;

    public int LockText { get; set; }

    public int IsCountingScore { get; set; }

    public List<int> StoryProgressList { get; set; } = [];

    public List<int> ClearProgressList { get; set; } = [];

    public override int GetId() => LevelID;

    public override void OnFinalize()
    {
        // 布局表关卡与 stage 派生集合取并集构成崩坏学园篇全图（IsNewMainStory），
        // 两处缺失 LevelMetaV2 行都说明客户端资源与服务器资源不同版本，必须启动期报错。
        LevelMetaV2 meta = GameTableCatalog.Instance.GetDataById<LevelMetaV2>(LevelID) ?? throw new InvalidDataException($"崩坏学园篇关卡 {LevelID} 不在 LevelMetaV2 中");
        meta.IsNewMainStory = true;
        if (meta.FirstClearRewards.Count == 0)
        {
            List<StoryRewardDefinition> rewards = [];
            RewardItemData.ExpandReward(meta.ClearReward1, 1, [], rewards);
            RewardItemData.ExpandReward(meta.ClearReward2, 1, [], rewards);
            meta.FirstClearRewards = Array.AsReadOnly(rewards.ToArray());
        }
    }
}

/// <summary>
/// 客户端 MoleMole.RX2LevelData 对应的 TSV 表项。
/// </summary>
public sealed class RX2LevelData : TableToolsTableBase
{
    public const string TablePath = "Data/RX2LevelData";

    public int LevelID { get; set; }

    public int SiteID { get; set; }

    public int StoryLine { get; set; }

    public int ChapterOrder { get; set; }

    public int TimeLineOrder { get; set; }

    public List<int> UnlockTrigger { get; set; } = [];

    public override int GetId() => LevelID;
}

/// <summary>
/// 客户端 MoleMole.GeneralTaskData 对应的 TSV 表项（章节任务书）。
/// 一行 = 书的一页；同 BookID 多行按 PageID 区分，GetId 用复合键保证唯一。
/// StoryType 为 GeneralStageData[stage].additionalStoryType（主线 RX0/2/3/4=59、RX1=61、
/// 支线 PRX/BRX=57 无书）；SeasonID 为 0 时表示该书不按赛季区分。
/// </summary>
public sealed class GeneralTaskData : TableToolsTableBase
{
    public const string TablePath = "Data/GeneralTaskData";

    public int BookID { get; set; }

    public int StoryType { get; set; }

    public int SeasonID { get; set; }

    public int PageID { get; set; }

    public List<int> TaskList { get; set; } = [];

    public List<int> LevelGroup { get; set; } = [];

    public int PageText { get; set; }

    public int PageName { get; set; }

    public int PartnerOffsetID { get; set; }

    public override int GetId() => BookID * 100 + PageID;

    public override void Verification()
    {
        foreach (int taskId in TaskList.Where(value => value != 0))
        {
            GeneralTaskRequest request = GameTableCatalog.Instance.GetDataById<GeneralTaskRequest>(taskId) ?? throw new InvalidDataException($"GeneralTaskData 书 {BookID} 页 {PageID} 引用了不存在的任务 {taskId}");
            foreach (int rewardId in request.RewardList.Where(value => value != 0))
            {
                GeneralTaskReward reward = GameTableCatalog.Instance.GetDataById<GeneralTaskReward>(rewardId) ?? throw new InvalidDataException($"GeneralTaskRequest {taskId} 引用了不存在的奖励 {rewardId}");
                if (reward.Type == 8)
                {
                    foreach (int rewardItemId in reward.Para1.Where(value => value != 0))
                    {
                        if (GameTableCatalog.Instance.GetDataById<RewardItemData>(rewardItemId) is null)
                        {
                            throw new InvalidDataException($"GeneralTaskReward {rewardId} 引用了不存在的 RewardItemData {rewardItemId}");
                        }
                    }
                }
            }
        }
    }
}

public sealed class GeneralTaskRequest : TableToolsTableBase
{
    public const string TablePath = "Data/GeneralTaskRequest";

    public int TaskID { get; set; }

    public int ChainID { get; set; }

    public int Type { get; set; }

    public int Repeatable { get; set; }

    public List<int> RewardList { get; set; } = [];

    public int ShortText { get; set; }

    public int LongText { get; set; }

    public int ShortTextClear { get; set; }

    public int LongTextClear { get; set; }

    public int RequestType { get; set; }

    public List<int> Para1 { get; set; } = [];

    public int Para2 { get; set; }

    public override int GetId() => TaskID;
}

/// <summary>
/// 客户端 MoleMole.GeneralTaskReward 对应的 TSV 表项（章节任务奖励）。
/// 客户端枚举 RewardType：1=UnlockLevel、2=Props、3=EnterLevelTrigger、
/// 4=TimesGroupScore、5=Coin、6=Score、8=NewMainStory；7 不在枚举内（Sedi 剧情进度）。
/// 崩坏学园篇任务书实际引用 1/2/7/8：1 的 Para1 是关卡 ID（本服章节关卡登录期全量
/// Doing 放出，解锁语义天然满足）；2 与 8 同构（客户端 MonoNewMainStoryMissionItem.Refresh
/// 对 type==8||type==2 统一按 Para1→RewardItemData 渲染与入账）；7 的 Para1/Para2 是
/// 平行列表（关卡→剧情进度，目标关卡即 NewMainStoryChapterData.StoryProgressList 引用
/// 剧情任务的枢纽关，随 9362 的 story_progress 下发）；8 的 Para1 引用 RewardItemData，
/// Para2[0] 是非 Sedi 玩法的限时条件奖励（崩坏学园篇全走 Sedi 路径，官服 9235 只回 Para1）。
/// 3/4/5/6 属于其他玩法（游乐园/肉鸽/银级）的任务书，本服务端未接入。
/// </summary>
public sealed class GeneralTaskReward : TableToolsTableBase
{
    public const string TablePath = "Data/GeneralTaskReward";

    public int RewardID { get; set; }

    public int Type { get; set; }

    public List<int> Para1 { get; set; } = [];

    public List<int> Para2 { get; set; } = [];

    public override int GetId() => RewardID;
}
