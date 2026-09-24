using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sv.Resources.Tables;

public abstract class MainlineTable : TableBase
{
    public int Id { get; protected set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; set; } = [];

    public override int GetId() => Id;

    public override void SetKey(JsonElement key)
    {
        if (key.ValueKind == JsonValueKind.Number) Id = key.GetInt32();
        else if (key.ValueKind == JsonValueKind.String) Id = int.Parse(key.GetString()!, CultureInfo.InvariantCulture);
        else
        {
            JsonElement[] parts = Elements(key);
            Id = checked(parts[0].GetInt32() * 10000 + parts[1].GetInt32());
        }
    }

    public bool Has(string name) => Fields.ContainsKey(name);
    public JsonElement Get(string name) => Fields.GetValueOrDefault(name);
    public int Int(string name, int fallback = 0) => Number(Get(name), fallback);
    public string Text(string name, string fallback = "") => Get(name).ValueKind == JsonValueKind.String ? Get(name).GetString()! : fallback;
    public bool Flag(string name) => Get(name).ValueKind == JsonValueKind.True || Int(name) != 0;
    public int[] Ints(string name) => Elements(Get(name)).Select(value => Number(value)).ToArray();
    public JsonElement[] List(string name) => Elements(Get(name));
    public Dictionary<int, JsonElement> Map(string name) => IntMap(Get(name));

    public static int Number(JsonElement value, int fallback = 0)
    {
        if (value.ValueKind == JsonValueKind.Number) return (int)value.GetDouble();
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int number)) return number;
        return fallback;
    }

    public static JsonElement[] Elements(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$tuple", out JsonElement tuple)) value = tuple;
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    }

    public static Dictionary<int, JsonElement> IntMap(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return [];
        if (value.TryGetProperty("$map", out JsonElement map))
        {
            return map.EnumerateArray().ToDictionary(pair => Number(pair[0]), pair => pair[1]);
        }
        return value.EnumerateObject().ToDictionary(pair => int.Parse(pair.Name, CultureInfo.InvariantCulture), pair => pair.Value);
    }

    public static object? Decode(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out long integer) ? (object)integer : value.GetDouble(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().Select(Decode).ToArray(),
            JsonValueKind.Object when value.TryGetProperty("$tuple", out JsonElement tuple) => Decode(tuple),
            JsonValueKind.Object when value.TryGetProperty("$map", out _) => IntMap(value).ToDictionary(pair => pair.Key.ToString(), pair => Decode(pair.Value)),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(pair => pair.Name, pair => Decode(pair.Value)),
            _ => null,
        };
    }
}

public sealed class EventContentData : MainlineTable
{
    public int DialogueId => Int("dialog");
    public int Battle => Int("enterBattle");
    public bool Emergency => Flag("isEmergencyEvent");
    public bool Patrol => Flag("patrolEvent");
    public int[] NextEvents => Ints("events");
    public bool Released => Flag("release");
    public bool RepeatableEvent => Flag("repeatable");
    public bool IsPresentationOnly => !Has("dialog") && !Has("enterBattle") && !Has("ui") && !Has("newbee_event");
    public Dictionary<string, JsonElement> CompletionConditions => Get("completeConds").ValueKind == JsonValueKind.Object
        ? Get("completeConds").EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value) : [];
}

public sealed class EventConditionData : MainlineTable
{
    public Dictionary<int, string> Conditions { get; private set; } = [];
    public Dictionary<int, ResourceCondition> Rules { get; } = [];
    public Dictionary<int, string> Unsupported { get; } = [];

    public override void OnLoad()
    {
        Conditions = Map("conds").ToDictionary(pair => pair.Key, pair => pair.Value.GetString() ?? "");
        foreach ((int type, string expression) in Conditions)
        {
            try { Rules[type] = new ResourceCondition(type, expression); }
            catch (FormatException ex) { Unsupported[type] = ex.Message; }
        }
    }
}

public sealed class EventDialogueData : MainlineTable
{
    public bool AllowsOption(int selectedEvent, int option)
    {
        if (option <= 0) return false;
        JsonElement[] options = List("opts");
        if (options.Length == 0) return Int("finish_event") == selectedEvent && option == 1;
        return options.Any(value => Number(value) == option) && Int($"opt{option}_event") == selectedEvent;
    }
}

public sealed class NewbeeData : MainlineTable
{
    public int Next => Int("next_newbee");
}

public sealed class MissionData : MainlineTable
{
    public int Area => Int("ch");
    public int[] Prerequisites => Ints("cond");
    public int[] NextStages => Ints("nextstagenums");
    public int ActionCost => Int("va");
    public int FatigueCost => Int("fatigue");
    public int[] PresetHeroes => Ints("defaultHeroes");
}

public sealed class RewardData : MainlineTable
{
    public IEnumerable<(int Item, int Count)> Items()
    {
        int[] items = Ints("items");
        int[] counts = Ints("itemsNum");
        if (items.Length != counts.Length) throw new InvalidDataException($"Reward {Id}: items/itemsNum mismatch");
        return items.Zip(counts);
    }
}

public sealed class CityFirstWeekData : MainlineTable;
public sealed class FightFirstWeekData : MainlineTable;
public sealed class EndingData : MainlineTable;
public sealed class MissionHeroData : MainlineTable
{
    public static int MakeId(int mission, int hero)
    {
        if (hero is < 0 or >= 512) throw new InvalidDataException("Mission hero key exceeds its reserved range");
        return checked(mission * 512 + hero);
    }

    public override void SetKey(JsonElement key)
    {
        JsonElement[] values = Elements(key);
        Id = MakeId(Number(values[0]), Number(values[1]));
    }
}
public sealed class CityFightRewardData : MainlineTable;
public sealed class BuildRewardData : MainlineTable;
public sealed class DevelopRewardData : MainlineTable;
public sealed class PatrolRewardData : MainlineTable;
public sealed class RestMoneyData : MainlineTable;
public sealed class RestFatigueData : MainlineTable;
public sealed class RestExperienceData : MainlineTable;
public sealed class PlayerExperienceData : MainlineTable;
public sealed class IntelligenceAffectData : MainlineTable;
public sealed class CityWeekBuildingData : MainlineTable;
public sealed class PrivateMessageData : MainlineTable;
public sealed class TerminalMessageData : MainlineTable;
public sealed class IntelligenceData : MainlineTable;
public sealed class MainlineWeekData : MainlineTable;
public sealed class FatigueRecoveryData : MainlineTable;
public sealed class IntelligenceRefreshData : MainlineTable;
public sealed class RandomRewardData : MainlineTable
{
    public int[][] WeightedEntries => List("weights").Select(value => Elements(value).Select(part => Number(part)).ToArray()).ToArray();
}
public sealed class HeroTreasureData : MainlineTable;
public sealed class HeroStarAttributeData : MainlineTable;
public sealed class CgData : MainlineTable;
public sealed class WeekendScoreData : MainlineTable;
public sealed class WeekendConditionData : MainlineTable;
public sealed class WeekendCityScoreData : MainlineTable;
