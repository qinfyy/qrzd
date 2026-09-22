using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

/// <summary>
/// 客户端 MoleMole.DeepSeaLevelData 对应的 DeepSeaLevelData.tsv 表项。
/// 字段名称和列表含义保持客户端策划表结构，服务端只在构造玩法响应时读取。
/// </summary>
public sealed class DeepSeaLevelData : TableToolsTableBase
{
    public const string TablePath = "Data/DeepSeaLevelData";

    public int LevelID { get; set; }

    public List<int> ModID { get; set; } = [];

    public int StartMod { get; set; }

    public List<int> EnemyExpect { get; set; } = [];

    public List<int> EnemyText { get; set; } = [];

    public List<string> SkillExpect { get; set; } = [];

    public List<int> SkillText { get; set; } = [];

    public bool IsAssist { get; set; }

    [Name("S_score")]
    public int SScore { get; set; }

    [Name("A_score")]
    public int AScore { get; set; }

    [Name("B_score")]
    public int BScore { get; set; }

    [Name("C_score")]
    public int CScore { get; set; }

    public int BasePoint { get; set; }

    public int TimePara { get; set; }

    public int TimeMin { get; set; }

    public int TimeMax { get; set; }

    public int ComboPara { get; set; }

    public int ComboMin { get; set; }

    public int ComboMax { get; set; }

    public List<float> ModBonus { get; set; } = [];

    public List<float> FloatingBonus { get; set; } = [];

    public float FriendBase { get; set; }

    public float DeathBase { get; set; }

    public override int GetId() => LevelID;
}
