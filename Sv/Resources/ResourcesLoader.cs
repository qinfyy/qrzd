using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Sv.Configuration;
using Sv.Resources.Serialization;
using Sv.Resources.Tables;

namespace Sv.Resources;

public static class ResourcesLoader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ResourcesLoader));

    private static GameTableCatalog _catalog = null!;
    private static string _dbxRoot = null!;
    private static JsonSerializerOptions _jsonOptions = null!;

    public static void Initialize(GameDataOptions options)
    {
        GameTableCatalog.Instance = _catalog = new GameTableCatalog();

        if (string.IsNullOrWhiteSpace(options.TableRoot))
        {
            Logger.Information("未配置 GameData:TableRoot，跳过游戏资源加载");
            return;
        }

        string root = Path.GetFullPath(options.TableRoot);
        _dbxRoot = Directory.Exists(Path.Combine(root, "dbx")) ? Path.Combine(root, "dbx") : root;

        if (!Directory.Exists(_dbxRoot))
        {
            Logger.Warning("策划表数据目录不存在: {DbxRoot}，跳过资源加载", _dbxRoot);
            return;
        }

        Stopwatch sw = Stopwatch.StartNew();
        Logger.Information("正在从 {DbxRoot} 加载策划配置表资源...", _dbxRoot);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters =
            {
                new DbxTupleListConverterFactory(),
                new DbxMapDictionaryConverterFactory(),
                new DbxStringJsonConverter(),
                new DbxByteArrayJsonConverter(),
                new DbxFloatJsonConverter(),
                new DbxDoubleJsonConverter(),
                new DbxValueTupleConverterFactory(),
            }
        };

        // 顺序反序列化并触发各行 OnLoad
        LoadTable<HeroData>(HeroData.TableFileName);
        LoadTable<HeroStarSkillData>(HeroStarSkillData.TableFileName);
        LoadTable<HeroStarData>(HeroStarData.TableFileName);
        LoadTable<ArtifactLevelData>(ArtifactLevelData.TableFileName);
        LoadTable<ArtifactAttrLevelData>(ArtifactAttrLevelData.TableFileName);
        LoadTable<AwakeConsumeData>(AwakeConsumeData.TableFileName);
        LoadTable<LiberateLvData>(LiberateLvData.TableFileName);
        LoadTable<LiberateTupoData>(LiberateTupoData.TableFileName);
        LoadTable<ItemData>(ItemData.TableFileName);
        LoadTable<HeroFragmentItemData>(HeroFragmentItemData.TableFileName);
        LoadTable<CityData>(CityData.TableFileName);
        LoadTable<CityBuildingData>(CityBuildingData.TableFileName);
        LoadTable<CityDevelopmentData>(CityDevelopmentData.TableFileName);
        LoadTable<CityUpgradeData>(CityUpgradeData.TableFileName);
        LoadTable<CityPatrolData>(CityPatrolData.TableFileName);
        LoadTable<BuildingData>(BuildingData.TableFileName);
        LoadTable<EventContentData>("event_content");
        LoadTable<EventConditionData>("event_condition");
        LoadTable<EventDialogueData>("event_dialogue");
        LoadTable<NewbeeData>("newbee");
        LoadTable<MissionData>("mission");
        LoadTable<RewardData>("reward_common");
        LoadTable<CityFirstWeekData>("city_first_week_data");
        LoadTable<FightFirstWeekData>("fight_first_week_data");
        LoadTable<EndingData>("ending_data");
        LoadTable<MissionHeroData>("mission_hero_attr");
        LoadTable<CityFightRewardData>("city_fight_reward");
        LoadTable<BuildRewardData>("build_reward");
        LoadTable<DevelopRewardData>("develop_reward");
        LoadTable<PatrolRewardData>("patrol_reward");
        LoadTable<RestMoneyData>("zhai_money");
        LoadTable<RestFatigueData>("zhai_fatigue");
        LoadTable<RestExperienceData>("zhai_exp");
        LoadTable<PlayerExperienceData>("player_exp");
        LoadTable<IntelligenceAffectData>("intelligence_affect");
        LoadTable<CityWeekBuildingData>("city_week_building");
        LoadTable<PrivateMessageData>("private_msg");
        LoadTable<TerminalMessageData>("social_msg");
        LoadTable<IntelligenceData>("intelligence");
        LoadTable<MainlineWeekData>("mainline_week_info");
        LoadTable<FatigueRecoveryData>("fatigue_recover");
        LoadTable<IntelligenceRefreshData>("refresh_intelligence");
        LoadTable<RandomRewardData>("reward_random");
        LoadTable<HeroTreasureData>("hero_treasure");
        LoadTable<HeroStarAttributeData>("hero_star_attr");
        LoadTable<CgData>("cg_data");
        LoadTable<WeekendScoreData>("weekend_score");
        LoadTable<WeekendConditionData>("weekend_cond");
        LoadTable<WeekendCityScoreData>("weekend_cityscore");

        // 构建跨表外键引用与二级索引
        _catalog.BroadcastOnFinalize();

        // 跨表约束与完整性校验
        _catalog.BroadcastVerification();

        _catalog.IsLoaded = true;
        sw.Stop();

        Logger.Information("策划配置表资源加载完成，已载入 {Count} 张表，耗时 {ElapsedMs} ms",
            _catalog.LoadedTableCount, sw.ElapsedMilliseconds);
    }

    public static void LoadTable<T>(string fileName) where T : TableBase
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".json";
        }

        string filePath = Path.Combine(_dbxRoot, fileName);
        if (!File.Exists(filePath))
        {
            Logger.Warning("策划表文件不存在: {FilePath}", filePath);
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            DbxTableFile<T>? tableFile = JsonSerializer.Deserialize<DbxTableFile<T>>(bytes, _jsonOptions);

            if (tableFile is null || tableFile.Rows.Count == 0)
            {
                Logger.Debug("策划表 {FileName} 为空或无数据行", fileName);
                _catalog.PushTable(new Dictionary<int, T>());
                return;
            }

            Dictionary<int, T> dataMap = new(tableFile.Rows.Count);

            foreach (DbxTableRow<T> row in tableFile.Rows)
            {
                if (row.Value is null) continue;

                row.Value.SetKey(row.Key);
                row.Value.OnLoad();

                int id = row.Value.GetId();
                if (id == 0 && row.Key.ValueKind == JsonValueKind.Number && row.Key.TryGetInt32(out int keyId))
                {
                    id = keyId;
                }

                dataMap[id] = row.Value;
            }

            _catalog.PushTable(dataMap);
            Logger.Information("成功载入策划表 {TableName}，共 {RowCount} 行", typeof(T).Name, dataMap.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "解析策划表 {FileName} 出错", fileName);
            throw;
        }
    }

    public static void LoadJson<T>(string fileName) where T : class
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".json";
        }

        string filePath = Path.Combine(_dbxRoot, fileName);
        if (!File.Exists(filePath))
        {
            Logger.Warning("JSON 配置文件不存在: {FilePath}", filePath);
            return;
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        T? data = JsonSerializer.Deserialize<T>(bytes, _jsonOptions);
        if (data is not null)
        {
            _catalog.PushJson(data);
        }
    }
}
