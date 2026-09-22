using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

/// <summary>客户端播放回顾目录中的剧情篇章锚点。</summary>
public sealed class PlayBackStoryTitleData : TableToolsTableBase
{
    public const string TablePath = "Data/PlayBackStoryTitleData";

    public int ID { get; set; }
    public int Type { get; set; }
    public int ExType { get; set; }
    public int ExTitle { get; set; }
    public string ExTitleText { get; set; } = string.Empty;
    public int SeasonID { get; set; }
    public int SeasonTitle { get; set; }
    public string SeasonText { get; set; } = string.Empty;
    public string BannerID { get; set; } = string.Empty;
    public int BannerTitle { get; set; }
    public string BannerText { get; set; } = string.Empty;
    public int Title { get; set; }
    public string TitleText { get; set; } = string.Empty;
    public int DialogueID { get; set; }
    public bool IsSub { get; set; }
    public int UnlockLevel { get; set; }
    public int DialogueType { get; set; }

    public override int GetId() => ID;
}

public sealed class GeneralStoryBook : TableToolsTableBase
{
    public const string TablePath = "Data/GeneralStoryBook";
    public int BookID { get; set; }
    public int StageID { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public int UnlockLevel { get; set; }
    public int BookName { get; set; }
    public List<int> TabID { get; set; } = [];
    public List<int> TabName { get; set; } = [];
    public List<int> TabTitle { get; set; } = [];
    public override int GetId() => BookID;
}

public sealed class GeneralStorySection : TableToolsTableBase
{
    public const string TablePath = "Data/GeneralStorySection";
    public int SectionID { get; set; }
    public int SectionName { get; set; }
    public int StoryType { get; set; }
    public List<int> StoryID { get; set; } = [];
    public List<int> StoryName { get; set; } = [];
    public int BookID { get; set; }
    public int TabID { get; set; }
    public override int GetId() => SectionID;
}
