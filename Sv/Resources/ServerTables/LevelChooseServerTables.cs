using Sv.Gateway.Packets;
using Sv.Resources.Tables;

namespace Sv.Resources.ServerTables;

/// <summary>
/// 服务端使用的关卡入口调度表，对应资源中的 Data/ServerData/LevelChooseSchedule.tsv。
/// </summary>
public sealed class LevelChooseScheduleTableData : TableBase
{
    public const string TablePath = "ServerData/LevelChooseSchedule";
    /// <summary>新玩家教学入口的固定 LevelID（LevelChooseV2 的 NewPlayerTeaching 行）。</summary>
    public const int NewPlayerTeachingEntranceId = 122;

    public short LevelID { get; set; }

    public override int GetId() => LevelID;

    public int Type { get; set; }

    public int OpenState { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    /// <summary>未序列化进 ChooseLevelScheduleData 包体，语义待确认，保持原始整数列。</summary>
    public int Closing { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public int TextID { get; set; }

    public short Order { get; set; }

    public short LevelRequire { get; set; }

    public bool IsWebview { get; set; }

    public string WebviewURL { get; set; } = string.Empty;

    public int ImageID { get; set; }

    public string ImageURL { get; set; } = string.Empty;

    public short LevelRequireMax { get; set; }

    public short RevealLevel { get; set; }

    public bool IsBGMMute { get; set; }

    public List<int> Rewards { get; set; } = [];

    public int IsHideEndTime { get; set; }

    public override void Verification()
    {
        if (LevelID == NewPlayerTeachingEntranceId)
        {
            LevelChooseLocalData tutorialEntrance =
                GameTableCatalog.Instance.GetDataById<LevelChooseLocalData>(LevelID)
                ?? throw new InvalidDataException("LevelChooseV2 缺少 NewPlayerTeaching 入口 122");
            if (!string.Equals(
                    tutorialEntrance.Name,
                    "NewPlayerTeaching",
                    StringComparison.Ordinal) ||
                Type != (int)GameLevelCategory.NewPlayerTeaching ||
                OpenState != (int)ActivityStatus.ELCA_STATUS_PERIOD_1)
            {
                throw new InvalidDataException("NewPlayerTeaching 入口 122 配置与客户端版本不匹配");
            }

            return;
        }

        // 客户端 RefreshLevelChooseInfo 仅当 LevelChooseV2 包含此 LevelID 时才把
        // scheduleData 合并到 LevelList；主线入口与章节目录都属此类。主线入口
        // 由 LevelChooseMainStoryEntranceMetaData 哨兵单独校验存在性。
        HashSet<int> mainStoryEntranceIds = [.. GameTableCatalog.Instance.GetAllData<LevelChooseMainStoryEntranceMetaData>().Select(value => value.levelChooseID)];
        if (!mainStoryEntranceIds.Contains(LevelID) && GameTableCatalog.Instance.GetDataById<LevelChooseLocalData>(LevelID) is null)
        {
            throw new InvalidDataException(
                $"服务端 LevelChooseSchedule 的入口 {LevelID} 不在 LevelChooseV2 客户端资源表中");
        }

        if (ImageID <= 0)
        {
            throw new InvalidDataException(
                $"服务端 LevelChooseSchedule 的入口 {LevelID} 图片 ID 非法");
        }

        if (!Uri.TryCreate(ImageURL, UriKind.Absolute, out Uri? imageUri) ||
            (!string.Equals(imageUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(imageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"服务端 LevelChooseSchedule 的入口 {LevelID} 图片 URL 非法");
        }

        // 哨兵行执行一次表级校验：主线入口与新玩家教学 122 必须在 schedule 中存在
        // （客户端主菜单靠 LevelChooseLogic.LevelList 重建 LevelChooseRule；
        // 缺任意一条都会让该入口在 735 之后仍不可点）。203 ZeroDLC 等 DLC 入口
        // 不在本期 schedule 范围（与官服登录段 735 字节一致）。其他 schedule
        // 允许扩展（RX0-RX4、PRX、BRX 等章节目录）。
        if (ReferenceEquals(GameTableCatalog.Instance.GetAllData<LevelChooseScheduleTableData>()[0], this))
        {
            int[] requiredScheduleIds = [1, NewPlayerTeachingEntranceId, 200, 201, 202, 204];
            if (requiredScheduleIds.Any(id => GameTableCatalog.Instance.GetAllData<LevelChooseScheduleTableData>().All(value => value.LevelID != id)))
            {
                throw new InvalidDataException("服务端 LevelChooseSchedule 缺少主线入口或新玩家教学 122，与当前客户端版本不匹配");
            }
        }
    }
}
