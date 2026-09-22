using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public sealed class CityData : TableBase
{
    public const string TableFileName = "city_data.json";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = string.Empty;

    [JsonPropertyName("res")]
    public string Res { get; set; } = string.Empty;

    [JsonPropertyName("show_build")]
    public int ShowBuild { get; set; }

    [JsonPropertyName("show_upgrade")]
    public int ShowUpgrade { get; set; }

    [JsonPropertyName("show_patrol")]
    public int ShowPatrol { get; set; }

    [JsonPropertyName("virtual")]
    public int Virtual { get; set; }

    [JsonPropertyName("area_require")]
    public Dictionary<string, List<int>> AreaRequire { get; set; } = [];

    public override int GetId() => Id;

    public bool IsVirtualArea => Virtual > 0;
}

public sealed class CityBuildingData : TableBase
{
    public const string TableFileName = "city_building.json";

    private int _id;

    public int Id => _id;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = string.Empty;

    [JsonPropertyName("build_desc")]
    public string BuildDesc { get; set; } = string.Empty;

    [JsonPropertyName("tips_desc")]
    public string TipsDesc { get; set; } = string.Empty;

    [JsonPropertyName("force_val")]
    public int ForceVal { get; set; }

    [JsonPropertyName("dev_val")]
    public int DevVal { get; set; }

    [JsonPropertyName("research_val")]
    public int ResearchVal { get; set; }

    [JsonPropertyName("research_require")]
    public int ResearchRequire { get; set; }

    [JsonPropertyName("cr")]
    public int Cr { get; set; }

    [JsonPropertyName("fc")]
    public int Fc { get; set; }

    [JsonPropertyName("ac")]
    public int Ac { get; set; }

    [JsonPropertyName("init")]
    public int Init { get; set; }

    [JsonPropertyName("can_remove")]
    public int CanRemove { get; set; }

    [JsonPropertyName("ban")]
    public int Ban { get; set; }

    [JsonPropertyName("clientBan")]
    public int ClientBan { get; set; }

    [JsonPropertyName("pre")]
    public List<int> Pre { get; set; } = [];

    [JsonPropertyName("area_building_require")]
    public Dictionary<int, int> AreaBuildingRequire { get; set; } = [];

    public override void SetKey(JsonElement key)
    {
        if (key.ValueKind == JsonValueKind.Number && key.TryGetInt32(out int id))
        {
            _id = id;
        }
    }

    public override int GetId() => _id;
}

public sealed class CityDevelopmentData : TableBase
{
    public const string TableFileName = "city_development.json";

    public int CityId { get; set; }
    public int DevStep { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("level_require")]
    public int LevelRequire { get; set; }

    public static int MakeId(int cityId, int devStep) => (cityId << 16) | (devStep & 0xFFFF);

    public override void SetKey(JsonElement key)
    {
        if (key.ValueKind == JsonValueKind.Object && key.TryGetProperty("$tuple", out JsonElement tupleProp) && tupleProp.GetArrayLength() >= 2)
        {
            CityId = tupleProp[0].GetInt32();
            DevStep = tupleProp[1].GetInt32();
        }
        else if (key.ValueKind == JsonValueKind.Array && key.GetArrayLength() >= 2)
        {
            CityId = key[0].GetInt32();
            DevStep = key[1].GetInt32();
        }
    }

    public override int GetId() => MakeId(CityId, DevStep);
}

public sealed class CityUpgradeData : TableBase
{
    public const string TableFileName = "city_upgrade.json";

    public int CityId { get; set; }
    public int UpgradeLevel { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("action_consume")]
    public int ActionConsume { get; set; }

    [JsonPropertyName("fatigue")]
    public int Fatigue { get; set; }

    [JsonPropertyName("lead_require")]
    public int LeadRequire { get; set; }

    [JsonPropertyName("money_consume")]
    public int MoneyConsume { get; set; }

    [JsonPropertyName("money_output")]
    public int MoneyOutput { get; set; }

    public static int MakeId(int cityId, int upgradeLevel) => (cityId << 16) | (upgradeLevel & 0xFFFF);

    public override void SetKey(JsonElement key)
    {
        if (key.ValueKind == JsonValueKind.Object && key.TryGetProperty("$tuple", out JsonElement tupleProp) && tupleProp.GetArrayLength() >= 2)
        {
            CityId = tupleProp[0].GetInt32();
            UpgradeLevel = tupleProp[1].GetInt32();
        }
        else if (key.ValueKind == JsonValueKind.Array && key.GetArrayLength() >= 2)
        {
            CityId = key[0].GetInt32();
            UpgradeLevel = key[1].GetInt32();
        }
    }

    public override int GetId() => MakeId(CityId, UpgradeLevel);
}

public sealed class CityPatrolData : TableBase
{
    public const string TableFileName = "city_patrol.json";

    public int CityId { get; set; }
    public int PatrolCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("action_consume")]
    public int ActionConsume { get; set; }

    [JsonPropertyName("fatigue")]
    public int Fatigue { get; set; }

    [JsonPropertyName("favor_reward")]
    public int FavorReward { get; set; }

    [JsonPropertyName("ir")]
    public int Ir { get; set; }

    public static int MakeId(int cityId, int patrolCount) => (cityId << 16) | (patrolCount & 0xFFFF);

    public override void SetKey(JsonElement key)
    {
        if (key.ValueKind == JsonValueKind.Object && key.TryGetProperty("$tuple", out JsonElement tupleProp) && tupleProp.GetArrayLength() >= 2)
        {
            CityId = tupleProp[0].GetInt32();
            PatrolCount = tupleProp[1].GetInt32();
        }
        else if (key.ValueKind == JsonValueKind.Array && key.GetArrayLength() >= 2)
        {
            CityId = key[0].GetInt32();
            PatrolCount = key[1].GetInt32();
        }
    }

    public override int GetId() => MakeId(CityId, PatrolCount);
}
