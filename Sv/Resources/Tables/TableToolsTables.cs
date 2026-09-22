using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;


public sealed class TableToolsDatas
{
    [JsonPropertyName("tableDatas")]
    public List<TableToolsData> tableDatas { get; set; } = [];

    public TableToolsData? GetTableToolsDataById(int tableId) =>
        tableDatas.FirstOrDefault(value => value.TableId == tableId);

    public sealed class TableToolsData
    {
        public int TableId { get; set; }

        public string tablePath { get; set; } = string.Empty;

        public string classType { get; set; } = string.Empty;

        public string classPath { get; set; } = string.Empty;

        public int HeadLineAmount { get; set; } = 1;

        public List<string> constructorNames { get; set; } = [];

        public List<string> constructorTypes { get; set; } = [];

        public List<int> constructorUsage { get; set; } = [];

        public TableType tableType { get; set; }

        public string ConfigTableClassType { get; set; } = string.Empty;

        public string ConfigTableClassPath { get; set; } = string.Empty;

        public bool isDivineTable { get; set; }

        public List<string> DivideTableParams { get; set; } = [];

        public List<string> ReleaseTableParams { get; set; } = [];

        public bool AllSafeField { get; set; }

        public FieldNameTranslateType FieldNameTranslateTypeVal { get; set; }
    }

    public enum TableType
    {
        Excel = 0,
        Json = 1,
    }

    public enum FieldNameTranslateType
    {
        NoChange = 0,
        FirstLower = 1,
        FirstUpper = 2,
    }
}
