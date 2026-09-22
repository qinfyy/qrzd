using System.Globalization;
using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

public sealed class TextMapData : TableToolsTableBase
{
    public const string TablePath = "Data/TextMap";

    public int TEXT_ID { get; set; }

    public string CONTENT { get; set; } = string.Empty;

    public string comment { get; set; } = string.Empty;

    public override int GetId() => TEXT_ID;

    /// <summary>文本 ID 是否可解析；0 表示无文本引用，视为合法。</summary>
    public static bool Exists(int textId) => textId == 0 || GameTableCatalog.Instance.GetDataById<TextMapData>(textId) is not null;
}
public sealed class PriceData
{
    public int HIGH_EGG_PRICE { get; set; }
    public int MID_EGG_PRICE { get; set; }
    public int LOW_EGG_PRICE { get; set; }
    public int VIP_EGG_PRICE { get; set; }
    public int FESTIVAL_EGG_PRICE { get; set; }
    public int Activity_EGG_PRICE { get; set; }
    public int REVIVE_PRICE { get; set; }
    public int RECOVER_STAMINA_PRICE { get; set; }
    public int EXPAND_PACK_PRICE { get; set; }
    public int CREATE_COMMUNITY_PRICE { get; set; }
    public int COMMENT_GUIDE_HC_REWARD { get; set; }
    public int INVITE_ONE_PERSON_HC_REWARD { get; set; }
    public int COMMUNITY_BADGE_RESET_HC_PRICE { get; set; }
    public int COMMUNITY_BADGE_RESET_HC_MAX_PRICE { get; set; }
    public int RECOVER_PET_SYNC_PRICE { get; set; }
    public int BLACK_MARKET_REFRESH_PRICE { get; set; }
    public int KYUSYO_REVIVE_PRICE { get; set; }
    public int KYUSYO_RECOVER_STAMINA_PRICE { get; set; }
    public int TALENT_RESET_PRICE { get; set; }
    public int EXPAND_SECOND_BOX_PRICE { get; set; }
}
