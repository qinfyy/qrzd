using System.Text.Json;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public static class RewardLogic
{
    private static readonly HashSet<string> SupportedFields =
    ["id", "tradeType", "award_pool", "items", "itemsNum", "hero", "cg", "friends", "friendly", "fatigue", "money", "crystal", "exp", "buildFund", "summoncoin",
        "buildableBuilding", "destroyBuliding", "treasure", "treasureLevel", "treasureNum"];

    public static bool CanGrant(Player player, IEnumerable<int> rewardIds)
    {
        long money = player.Profile.Money;
        long crystal = player.Profile.Crystal;
        long exp = player.Profile.Experience;
        long coins = player.Profile.SummonCoin;
        long fund = player.City.BuildFund;
        Dictionary<int, int> items = player.Inventory.Counts();
        foreach (int id in rewardIds)
        {
            if (id == 0) continue;
            if (!GameTableCatalog.Instance.TryGetDataById<RewardData>(id, out var reward)) return false;
            if (reward.Fields.Keys.Any(field => !SupportedFields.Contains(field))) return false;
            if (reward.Ints("items").Length != reward.Ints("itemsNum").Length) return false;
            foreach (var item in reward.Items())
            {
                if (item.Count > 0 && !player.Inventory.CanGrant(item.Item, item.Count)) return false;
                long total = (long)items.GetValueOrDefault(item.Item) + item.Count;
                if (total is < 0 or > int.MaxValue) return false;
                items[item.Item] = (int)total;
            }
            if (reward.Ints("hero").Any(hero => !GameTableCatalog.Instance.TryGetDataById<HeroData>(hero, out _))) return false;
            if (reward.Ints("cg").Any(cg => !player.Story.CanAddCg(cg))) return false;
            int[] treasures = reward.Ints("treasure");
            int[] counts = reward.Ints("treasureNum");
            int[] levels = reward.Ints("treasureLevel");
            if (treasures.Length != counts.Length || treasures.Length != levels.Length) return false;
            if (treasures.Where((item, index) => !player.Inventory.CanGrantTreasure(item, levels[index], counts[index])).Any()) return false;
            if (reward.Ints("buildableBuilding").Any(building => GameTableCatalog.Instance.GetDataById<CityBuildingData>(building) is null)) return false;
            if (reward.List("destroyBuliding").Any(value => MainlineTable.Elements(value).Length != 2)) return false;
            money += reward.Int("money");
            crystal += reward.Int("crystal");
            exp += reward.Int("exp");
            coins += reward.Int("summoncoin");
            fund += reward.Int("buildFund");
            if (money is < 0 or > int.MaxValue || crystal is < 0 or > int.MaxValue || exp is < 0 or > int.MaxValue || fund is < 0 or > int.MaxValue) return false;
            if (reward.Int("exp") < 0 || coins is < 0 or > int.MaxValue) return false;
        }
        return true;
    }

    public static Dictionary<string, object> Grant(Player player, IEnumerable<int> rewardIds)
    {
        int[] ids = rewardIds.ToArray();
        if (!CanGrant(player, ids)) throw new InvalidOperationException("奖励配置尚不支持或资源缺失");
        Dictionary<string, object> result = [];
        List<object> items = [];
        List<int> heroes = [];
        List<int> cgs = [];
        List<object> treasures = [];
        int money = 0;
        int crystal = 0;
        int exp = 0;
        int coins = 0;
        foreach (int id in ids)
        {
            if (id == 0) continue;
            RewardData reward = GameTableCatalog.Instance.GetDataById<RewardData>(id)!;
            foreach ((int item, int count) in reward.Items())
            {
                if (count > 0) player.Inventory.Grant(item, count);
                if (count < 0 && !player.Inventory.Consume(item, -count)) throw new InvalidOperationException("道具不足");
                items.Add(new object[] { item, count });
            }
            int[] treasureIds = reward.Ints("treasure");
            for (int i = 0; i < treasureIds.Length; i++)
                treasures.AddRange(player.Inventory.GrantTreasure(treasureIds[i], reward.Ints("treasureLevel")[i], reward.Ints("treasureNum")[i]));
            foreach (int hero in reward.Ints("hero"))
            {
                if (player.HeroMgr.Unlock(hero)) heroes.Add(hero);
            }
            foreach (int cg in reward.Ints("cg"))
            {
                if (player.Story.AddCg(cg)) cgs.Add(cg);
            }
            foreach ((int hero, JsonElement value) in reward.Map("friendly")) player.HeroMgr.AddFriendly(hero, MainlineTable.Number(value));
            foreach ((int hero, JsonElement value) in reward.Map("friends")) player.HeroMgr.AddFriendly(hero, MainlineTable.Number(value));
            foreach ((int hero, JsonElement value) in reward.Map("fatigue")) player.HeroMgr.AddFatigue(hero, MainlineTable.Number(value));
            player.City.ApplyReward(reward);
            money = checked(money + reward.Int("money"));
            crystal = checked(crystal + reward.Int("crystal"));
            exp = checked(exp + reward.Int("exp"));
            coins = checked(coins + reward.Int("summoncoin"));
        }
        if (money != 0) { player.Profile.AddMoney(money); result["money"] = money; }
        if (crystal != 0) { player.Profile.AddCrystal(crystal); result["crystal"] = crystal; }
        if (exp != 0) Merge(result, player.Profile.AddExperience(exp));
        if (coins != 0) { player.Profile.AddSummonCoin(coins); result["summoncoin"] = coins; }
        if (items.Count != 0) result["item"] = items.ToArray();
        if (heroes.Count != 0) result["hero"] = heroes.ToArray();
        if (cgs.Count != 0) result["cg"] = cgs.ToArray();
        if (treasures.Count != 0) result["treasure"] = treasures.ToArray();
        return result;
    }

    public static bool CanGrantRandom(Player player, IEnumerable<int> ids) => ids.All(id =>
        GameTableCatalog.Instance.GetDataById<RandomRewardData>(id) is { } row && row.WeightedEntries.Length > 0 &&
        row.WeightedEntries.All(pair => pair.Length == 2 && pair[1] >= 0 && CanGrant(player, [pair[0]])) && row.WeightedEntries.Sum(pair => (long)pair[1]) is > 0 and <= int.MaxValue);

    public static Dictionary<string, object> GrantRandom(Player player, IEnumerable<int> ids)
    {
        int[] pools = ids.ToArray();
        if (!CanGrantRandom(player, pools)) throw new InvalidOperationException("随机奖励配置暂不支持");
        Dictionary<string, object> result = [];
        foreach (int id in pools)
        {
            int[][] weights = GameTableCatalog.Instance.GetDataById<RandomRewardData>(id)!.WeightedEntries;
            int roll = Random.Shared.Next(weights.Sum(pair => pair[1]));
            int reward = weights.First(pair => (roll -= pair[1]) < 0)[0];
            Merge(result, Grant(player, [reward]));
        }
        return result;
    }

    public static Dictionary<string, object> GrantAction(Player player, MainlineTable row, int actionCost)
    {
        int money = row.Int("money");
        Dictionary<string, object> reward = player.Profile.AddExperience(row.Int("exp"));
        player.Profile.AddMoney(money);
        reward["money"] = money;
        if (actionCost > 0 && GameTableCatalog.Instance.TryGetDataById<CityFirstWeekData>(player.City.TotalActions, out var first))
            Merge(reward, Grant(player, first.Ints("reward")));
        return reward;
    }

    public static void Merge(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        foreach ((string key, object value) in source)
        {
            if (target.TryGetValue(key, out object? previous) && previous is Array left && value is Array right)
                target[key] = left.Cast<object>().Concat(right.Cast<object>()).ToArray();
            else if (previous is int number && value is int addition && key is "money" or "crystal" or "exp") target[key] = number + addition;
            else target[key] = value;
        }
    }
}
