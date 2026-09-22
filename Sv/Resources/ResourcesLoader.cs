using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using Serilog;
using Sv.Configuration;
using Sv.Resources.ServerTables;
using Sv.Resources.Tables;
using Sv.Utility;

namespace Sv.Resources;

public static class ResourcesLoader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ResourcesLoader));

    private static GameTableCatalog _catalog = null!;
    private static string _dataRoot = null!;
    private static string _serverDataRoot = null!;
    private static TableToolsDatas? _tableTools;
    public static void Initialize(GameDataOptions options)
    {
        GameTableCatalog.Instance = _catalog = new GameTableCatalog();

        if (string.IsNullOrWhiteSpace(options.TableRoot))
        {
            Logger.Information("未配置 GameData:TableRoot，跳过游戏资源加载");
            return;
        }

        string tableRoot = Path.GetFullPath(options.TableRoot);
        _serverDataRoot = string.IsNullOrWhiteSpace(options.ServerDataPath) ? Path.Combine(AppContext.BaseDirectory, "ServerData") : options.ServerDataPath;
        _dataRoot = ResolveDataRoot(tableRoot);

        _tableTools = LoadTableToolsDatas(tableRoot, _dataRoot);

        LoadTable<WeaponDataV3>(WeaponDataV3.TablePath);
        LoadTable<CostumeDataV2>(CostumeDataV2.TablePath);
        LoadTable<PassiveSkillDataV3>(PassiveSkillDataV3.TablePath);
        LoadTable<StackMaterialData>(StackMaterialData.TablePath);
        LoadTable<EmblemData>(EmblemData.TablePath);
        LoadTable<PetChipData>(PetChipData.TablePath);
        LoadTable<SpecialAttributeDataV2>(SpecialAttributeDataV2.TablePath);
        LoadTable<RoleData>(RoleData.TablePath);
        LoadTable<RoleVoiceData>(RoleVoiceData.TablePath);
        LoadJson<Dictionary<string, PriceData>>("Data/PriceData");
        LoadTable<TalentData>(TalentData.TablePath);
        LoadTable<TalentConsumptionData>(TalentConsumptionData.TablePath);
        LoadTable<TalentSwitchData>(TalentSwitchData.TablePath);
        LoadTable<PowerupExpTypesData>(PowerupExpTypesData.TablePath);
        LoadTable<EquipEvolveData>(EquipEvolveData.TablePath);
        LoadTable<PromoteData>(PromoteData.TablePath);
        LoadTable<SkillUpMaterialData>(SkillUpMaterialData.TablePath);
        LoadTable<RoleDataV2>(RoleDataV2.TablePath);
        LoadTable<RoleLevelData>(RoleLevelData.TablePath);
        LoadTable<RoleSkillData>(RoleSkillData.TablePath);
        LoadDividedTable<TextMapData>(TextMapData.TablePath);
        LoadTable<RoleDuplicateData>(RoleDuplicateData.TablePath);
        LoadTable<RoleMissionData>(RoleMissionData.TablePath);
        LoadTable<RoleMissionRewardData>(RoleMissionRewardData.TablePath);
        // KeyID 在原表中允许同一计数下存在多个等级变体，不能作为唯一索引。
        LoadTable<RegularAssistEquipData>(RegularAssistEquipData.TablePath);
        LoadTable<DefaultEquipData>(DefaultEquipData.TablePath);
        LoadTable<LevelMetaV2>(LevelMetaV2.TablePath);
        LoadTable<PlayBackStoryTitleData>(PlayBackStoryTitleData.TablePath);
        LoadTable<GeneralStoryBook>(GeneralStoryBook.TablePath);
        LoadTable<GeneralStorySection>(GeneralStorySection.TablePath);
        LoadTable<RewardItemData>(RewardItemData.TablePath);
        LoadTable<RewardData>(RewardData.TablePath);
        // CG 图鉴：ValidateReward 的 awardType=49 分支与 CGComp 初始解锁集合都依赖本表。
        LoadTable<CGUnlockData>(CGUnlockData.TablePath);
        LoadTable<DeepSeaLevelData>(DeepSeaLevelData.TablePath);
        LoadTable<LevelChooseLocalData>(LevelChooseLocalData.TablePath);
        LoadTable<LevelChooseMainStoryEntranceMetaData>(LevelChooseMainStoryEntranceMetaData.TablePath);
        LoadServerTable<LevelChooseScheduleTableData>(LevelChooseScheduleTableData.TablePath, "LevelChooseSchedule.tsv");
        LoadServerTable<StoreOfferData>(StoreOfferData.TablePath, "StoreOffer.tsv");
        LoadServerTable<StoreItemData>(StoreItemData.TablePath, "StoreItem.tsv");
        LoadServerTable<StoreProductIdData>(StoreProductIdData.TablePath, "StoreProductId.tsv");
        LoadServerTable<StoreBannerData>(StoreBannerData.TablePath, "StoreBanner.tsv");
        LoadServerTable<StoreTagData>(StoreTagData.TablePath, "StoreTag.tsv");
        LoadServerTable<GachaPoolData>(GachaPoolData.TablePath, "GachaPool.tsv");
        LoadServerTable<GachaConfigTableData>(GachaConfigTableData.TablePath, "GachaConfig.tsv");
        LoadServerTable<GachaTicketStoreData>(GachaTicketStoreData.TablePath, "GachaTicketStore.tsv");
        LoadServerTable<GachaTicketMaterialData>(GachaTicketMaterialData.TablePath, "GachaTicketMaterial.tsv");
        LoadServerTable<GachaRuleData>(GachaRuleData.TablePath, "GachaRule.tsv");
        LoadTable<NewMainStoryMenuData>(NewMainStoryMenuData.TablePath);
        LoadTable<NewMainStoryChapterData>(NewMainStoryChapterData.TablePath);
        LoadTable<RX2LevelData>(RX2LevelData.TablePath);
        // 通用章节任务三张表：BookID+PageID 复合键（一书多页），任务/奖励按自身 ID
        LoadTable<GeneralTaskData>(GeneralTaskData.TablePath);
        LoadTable<GeneralTaskRequest>(GeneralTaskRequest.TablePath);
        LoadTable<GeneralTaskReward>(GeneralTaskReward.TablePath);
        LoadTable<PlayerGeneralLogicDataV2>(PlayerGeneralLogicDataV2.TablePath);
        LoadTable<TutorialData>(TutorialData.TablePath);
        LoadTable<WeakGuideData>(WeakGuideData.TablePath);
        LoadTable<NewPlayerDetentionV2>(NewPlayerDetentionV2.TablePath);
        // GuideData 的 GuideID 在多页图文中重复，使用客户端读取顺序作为唯一表行键。
        LoadTable<GuideData>(GuideData.TablePath);
        LoadTable<KyusyoData>(KyusyoData.TablePath);
        LoadTable<KyusyoLevelMeta>(KyusyoLevelMeta.TablePath);
        LoadTable<KyusyoMissionData>(KyusyoMissionData.TablePath);
        LoadTable<KyusyoExpoData>(KyusyoExpoData.TablePath);
        LoadTable<KyusyoMazeTreasureData>(KyusyoMazeTreasureData.TablePath);
        LoadTable<KyusyoMazeSubLevelData>(KyusyoMazeSubLevelData.TablePath);
        LoadTable<KyusyoMazeTriggerUnlockData>(KyusyoMazeTriggerUnlockData.TablePath);
        LoadTable<KyusyoWeaponData>(KyusyoWeaponData.TablePath);
        LoadTable<KyusyoStoryData>(KyusyoStoryData.TablePath);
        LoadTable<KyusyoStoryChoiceData>(KyusyoStoryChoiceData.TablePath);
        LoadTable<DlcStoryData>(DlcStoryData.TablePath);
        LoadTable<DlcStoryChoiceData>(DlcStoryChoiceData.TablePath);
        LoadTable<DlcRoleData>(DlcRoleData.TablePath);
        LoadTable<DlcRoleSettingData>(DlcRoleSettingData.TablePath);
        LoadTable<DlcEquipmentData>(DlcEquipmentData.TablePath);
        LoadTable<DlcAllAbilityData>(DlcAllAbilityData.TablePath);
        LoadTable<DlcTalentData>(DlcTalentData.TablePath);
        LoadTable<DlcRuneData>(DlcRuneData.TablePath);
        LoadTable<DlcRuneLinkData>(DlcRuneLinkData.TablePath);
        LoadTable<DlcAchieveData>(DlcAchieveData.TablePath);
        LoadTable<DlcMissionData>(DlcMissionData.TablePath);
        LoadTable<DlcLevelMetaData>(DlcLevelMetaData.TablePath);
        LoadTable<EquipSkinData>(EquipSkinData.TablePath);
        LoadTable<PlayerSkinData>(PlayerSkinData.TablePath);
        LoadTable<RPData>(RPData.TablePath);
        LoadTable<RPUpgradeRewardData>(RPUpgradeRewardData.TablePath);
        LoadTable<PartnerPosterData>(PartnerPosterData.TablePath);
        LoadTable<PartnerPosterChangeData>(PartnerPosterChangeData.TablePath);
        LoadTable<PartnerStoryHeadData>(PartnerStoryHeadData.TablePath);
        LoadTable<PartnerOffsetData>(PartnerOffsetData.TablePath);
        LoadTable<RPStoryRewardData>(RPStoryRewardData.TablePath);
        LoadTable<TaskData>(TaskData.TablePath);
        LoadJson<TutorialGeneralLogicData>(TutorialGeneralLogicData.TablePath);
        Dictionary<string, GeneralStageDataItem> stageItems = LoadJson<Dictionary<string, GeneralStageDataItem>>("Data/GeneralStageData");
        foreach ((string name, GeneralStageDataItem item) in stageItems)
        {
            item.Name = name;
        }
        LoadTable<AbyssStageData>(AbyssStageData.TablePath);

        // OnFinalize
        _catalog.BroadcastOnFinalize();

        // Verification
        _catalog.BroadcastVerification();

        _catalog.IsLoaded = true;

        Logger.Information("游戏资源加载完成，已加载表 {LoadedTableCount}，JSON {LoadedJsonCount}", _catalog.LoadedTableCount, _catalog.LoadedJsonCount);
    }

    private static void LoadTable<T>(string tablePath) where T : TableBase
    {
        string normalizedPath = NormalizeResourcePath(tablePath);
        int headLineAmount = _tableTools?.tableDatas.FirstOrDefault(v => string.Equals(v.tablePath, normalizedPath, StringComparison.OrdinalIgnoreCase))?.HeadLineAmount ?? 1;
        if (headLineAmount < 1)
        {
            throw new InvalidDataException($"表 {normalizedPath} 的表头行数 {headLineAmount} 非法");
        }

        string fullPath = ResolveFilePath(normalizedPath + ".tsv");
        Dictionary<int, T> dataMap = ReadTsvTable<T>(fullPath, headLineAmount);
        ProcessTableOnLoad(dataMap);
        _catalog.PushTable(dataMap);
        Logger.Information("资源表已加载，路径 {TablePath}，类型 {TableType}，记录 {Count}，表头 {HeadLineAmount} 行", normalizedPath, typeof(T).Name, dataMap.Count, headLineAmount);
    }

    private static void LoadServerTable<T>(string tablePath, string fileName) where T : TableBase
    {
        string normalizedPath = NormalizeResourcePath(tablePath);
        string fullPath = ResolveServerFilePath(fileName);

        Dictionary<int, T> dataMap = ReadTsvTable<T>(fullPath, headLineAmount: 1);
        ProcessTableOnLoad(dataMap);
        _catalog.PushTable(dataMap);
        Logger.Information("服务端资源表已加载，路径 {TablePath}，类型 {TableType}，记录 {Count}", normalizedPath, typeof(T).Name, dataMap.Count);
    }

    private static T LoadJson<T>(string resourcePath) where T : class
    {
        string normalizedPath = NormalizeResourcePath(resourcePath);
        string fullPath = ResolveFilePath(normalizedPath + ".json");

        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            T value = JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw new InvalidDataException($"JSON 资源 {normalizedPath} 内容为空");
            _catalog.PushJson(value);
            Logger.Information("JSON 资源已加载，路径 {ResourcePath}，类型 {ResourceType}", normalizedPath, typeof(T).Name);
            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"JSON 资源 {normalizedPath} 解析失败", exception);
        }
    }

    private static void LoadDividedTable<T>(string tablePath) where T : TableBase
    {
        string normalizedPath = NormalizeResourcePath(tablePath);
        int headLineAmount = _tableTools?.tableDatas.FirstOrDefault(v => string.Equals(v.tablePath, normalizedPath, StringComparison.OrdinalIgnoreCase))?.HeadLineAmount ?? 1;
        string[] fragmentPaths = ResolveDividedFilePaths(normalizedPath);

        Dictionary<int, T> dataById = [];
        foreach (string fragmentPath in fragmentPaths)
        {
            Dictionary<int, T> fragmentMap = ReadTsvTable<T>(fragmentPath, headLineAmount, CsvMode.NoEscape);
            foreach ((int id, T data) in fragmentMap)
            {
                if (!dataById.TryAdd(id, data))
                {
                    throw new InvalidDataException($"分片资源表 {normalizedPath} 跨分片存在重复 ID {id}");
                }
            }
        }

        ProcessTableOnLoad(dataById);
        _catalog.PushTable(dataById);
        Logger.Information("分片资源表已加载，路径 {TablePath}，类型 {TableType}，分片 {FragmentCount}，记录 {Count}", normalizedPath, typeof(T).Name, fragmentPaths.Length, dataById.Count);
    }

    private static string ResolveFilePath(string normalizedPathWithExtension)
    {
        string relative = normalizedPathWithExtension.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) ? normalizedPathWithExtension["Data/".Length..] : normalizedPathWithExtension;
        string fullPath = Path.Combine(_dataRoot, relative);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"资源文件不存在: {fullPath}", fullPath);
        }

        return fullPath;
    }

    private static string ResolveServerFilePath(string fileName)
    {
        string fullPath = Path.Combine(_serverDataRoot, fileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"服务端资源表不存在: {fullPath}", fullPath);
        }

        return fullPath;
    }

    private static string NormalizeResourcePath(string resourcePath)
    {
        string normalizedPath = resourcePath.Replace('\\', '/').Trim().Trim('/');
        if (!normalizedPath.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = $"Data/{normalizedPath}";
        }

        return normalizedPath;
    }

    private static string[] ResolveDividedFilePaths(string normalizedPath)
    {
        string relative = normalizedPath.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) ? normalizedPath["Data/".Length..] : normalizedPath;

        string directory = Path.Combine(_dataRoot, Path.GetDirectoryName(relative) ?? string.Empty);
        string baseFileName = Path.GetFileName(relative);
        string prefix = baseFileName + "_";

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"分片资源目录不存在: {directory}");
        }

        string[] fragmentPaths = Directory.EnumerateFiles(directory, prefix + "*.tsv", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(path);
                string suffix = nameWithoutExt[prefix.Length..];
                return (Path: path, Valid: int.TryParse(suffix, CultureInfo.InvariantCulture, out int index), Index: index);
            })
            .Where(v => v.Valid)
            .OrderBy(v => v.Index)
            .Select(v => v.Path)
            .ToArray();

        if (fragmentPaths.Length == 0)
        {
            throw new InvalidDataException($"分片资源表 {normalizedPath} 没有数据分片");
        }

        return fragmentPaths;
    }

    private static string ResolveDataRoot(string tableRoot)
    {
        if (!Directory.Exists(tableRoot))
        {
            throw new DirectoryNotFoundException($"游戏资源目录不存在: {tableRoot}");
        }

        if (string.Equals(Path.GetFileName(tableRoot.TrimEnd(Path.DirectorySeparatorChar)), "Data", StringComparison.OrdinalIgnoreCase))
        {
            return tableRoot;
        }

        string dataRoot = Path.Combine(tableRoot, "Data");
        if (!Directory.Exists(dataRoot))
        {
            throw new DirectoryNotFoundException($"游戏资源目录缺少 Data 子目录: {tableRoot}");
        }

        return dataRoot;
    }

    private static TableToolsDatas LoadTableToolsDatas(string tableRoot, string dataRoot)
    {
        string? metadataPath = FindTableToolsDataPath(tableRoot, dataRoot);
        if (metadataPath is null)
        {
            throw new FileNotFoundException("游戏资源目录中缺少 TableToolsData 元目录");
        }

        using FileStream stream = File.OpenRead(metadataPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement tableDatasElement;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            tableDatasElement = document.RootElement;
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(document.RootElement, "tableDatas", out JsonElement value))
        {
            tableDatasElement = value;
        }
        else
        {
            throw new InvalidDataException("TableToolsData 不含 tableDatas 数组");
        }

        List<TableToolsDatas.TableToolsData>? tableDatas = JsonSerializer.Deserialize<
            List<TableToolsDatas.TableToolsData>>(tableDatasElement.GetRawText());
        if (tableDatas is null || tableDatas.Count == 0)
        {
            throw new InvalidDataException("TableToolsData 元目录为空");
        }

        HashSet<int> tableIds = [];
        HashSet<string> tablePaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (TableToolsDatas.TableToolsData tableData in tableDatas)
        {
            if (!tableIds.Add(tableData.TableId))
            {
                throw new InvalidDataException($"TableToolsData 存在重复 TableId {tableData.TableId}");
            }

            string normalizedPath = NormalizeResourcePath(tableData.tablePath);
            tableData.tablePath = normalizedPath;
            if (!tablePaths.Add(normalizedPath))
            {
                throw new InvalidDataException($"TableToolsData 存在重复路径 {normalizedPath}");
            }
        }

        return new TableToolsDatas { tableDatas = tableDatas };
    }

    private static string? FindTableToolsDataPath(string tableRoot, string dataRoot)
    {
        DirectoryInfo rootDirectory = new(tableRoot);
        string? extractionRoot = rootDirectory.Parent?.Parent?.FullName;
        string[] candidates =
        [
            Path.Combine(dataRoot, "jsondata", "TableToolsData.json"),
            Path.Combine(tableRoot, "metadata", "tabletoolsdata.json"),
            extractionRoot is null ? string.Empty : Path.Combine(extractionRoot, "metadata", "tabletoolsdata.json"),
        ];
        return candidates.FirstOrDefault(path => path.Length > 0 && File.Exists(path));
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Dictionary<int, T> ReadTsvTable<T>(string fullPath, int headLineAmount, CsvMode mode = CsvMode.RFC4180) where T : TableBase
    {
        CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = "\t",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            // TableTools 的中文说明行不保证与字段行列数一致，只校验实际数据区。
            DetectColumnCountChanges = false,
            PrepareHeaderForMatch = args => args.Header
                .TrimStart('\uFEFF')
                .ToUpperInvariant(),
            Mode = mode,
        };

        try
        {
            using StreamReader reader = new(fullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using CsvReader csv = new(reader, configuration);
            RegisterTableValueConverters(csv.Context.TypeConverterCache);
            if (!csv.Read())
            {
                throw new InvalidDataException($"资源表 {fullPath} 为空");
            }

            csv.ReadHeader();
            int columnCount = csv.HeaderRecord?.Length ?? throw new InvalidDataException($"资源表 {fullPath} 缺少字段表头");
            for (int line = 1; line < headLineAmount; line++)
            {
                if (!csv.Read())
                {
                    throw new InvalidDataException($"资源表 {fullPath} 不足 {headLineAmount} 行表头");
                }
            }

            Dictionary<int, T> dataById = [];
            int rowIndex = 0;
            while (csv.Read())
            {
                if (csv.Parser.Count != columnCount)
                {
                    throw new InvalidDataException($"资源表 {fullPath} 第 {csv.Parser.Row} 行列数 {csv.Parser.Count}，预期 {columnCount}");
                }

                T data = csv.GetRecord<T>();
                if (data is TableToolsTableBase tableToolsData)
                {
                    tableToolsData.SetDataItemIndex(rowIndex++);
                }

                int id = data.GetId();
                if (!dataById.TryAdd(id, data))
                {
                    throw new InvalidDataException($"资源表 {fullPath} 存在重复 ID {id}");
                }
            }

            return dataById;
        }
        catch (CsvHelperException exception)
        {
            throw new InvalidDataException($"资源表 {fullPath} 第 {exception.Context?.Parser?.Row ?? 0} 行解析失败", exception);
        }
    }

    private static void ProcessTableOnLoad<T>(Dictionary<int, T> dataMap) where T : TableBase
    {
        foreach (T item in dataMap.Values)
        {
            item.OnLoad();
        }
    }

    private static void RegisterTableValueConverters(TypeConverterCache converters)
    {
        converters.AddConverter<int>(new TableInt32Converter());
        converters.AddConverter<float>(new TableSingleConverter());
        converters.AddConverter<bool>(new TableBooleanConverter());
        converters.AddConverter<DateTimeOffset>(new TableDateTimeOffsetConverter());
        converters.AddConverter<List<int>>(new TableListConverter<int>(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)));
        converters.AddConverter<List<float>>(new TableListConverter<float>(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)));
        converters.AddConverter<List<string>>(new TableListConverter<string>(value => value));
        converters.AddConverter<List<bool>>(new TableListConverter<bool>(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) > 0));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class TableInt32Converter : Int32Converter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return base.ConvertFromString(text, row, memberMapData)!;
        }
    }

    private sealed class TableSingleConverter : SingleConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            return base.ConvertFromString(text, row, memberMapData)!;
        }
    }

    private sealed class TableBooleanConverter : BooleanConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value != 0;

            return base.ConvertFromString(text, row, memberMapData)!;
        }
    }

    /// <summary>
    /// 表内时间列同时接受两种写法：
    /// <list type="bullet">
    /// <item><c>yyyy-MM-ddTHH:mm:ss</c> —— 推荐写法，不带时区标记时按**配置时区**（Server:TimeZone）解释；</item>
    /// <item>Unix 秒（纯整数）—— 兼容旧数据，便于逐表迁移。</item>
    /// </list>
    /// 空单元格与 <c>0</c> 一律表示未设置，返回 null，因此时间列必须声明成 <c>DateTimeOffset?</c>；
    /// 非空属性收到 null 会在赋值时抛异常。
    /// </summary>
    private sealed class TableDateTimeOffsetConverter : DateTimeOffsetConverter
    {
        private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";

        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null!;
            }

            // 先按 DateTime 读再挂配置时区的偏移：DateTimeOffset 直接解析会按系统时区解释。
            if (DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime readable))
            {
                return Time.FromLocal(readable);
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
            {
                // 同样归一到配置时区偏移，使两种写法的 DateTimeOffset 呈现一致。
                return seconds == 0 ? null! : Time.FromUnixSeconds(seconds);
            }

            // 两种写法都不匹配时交给基类抛出 TypeConverterException，由 ReadTsvTable 包装成带行号的 InvalidDataException。
            return base.ConvertFromString(text, row, memberMapData)!;
        }
    }

    private sealed class TableListConverter<T>(Func<string, T> convertValue) : DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            string value = string.IsNullOrEmpty(text) ? "0" : text;
            return value.Split([';', '；'])
                .Where(value => value.Length > 0)
                .Select(convertValue)
                .ToList();
        }
    }
}
