using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class HeroMgrLogic(Player player) : PlayerLogicBase(player)
{
    private static readonly string[] ArtifactAttributes = ["_base_max_hp", "_base_phy_att_str", "_base_mag_att_str", "_base_phy_def", "_base_mag_def"];

    private HeroMgrComp Comp => Player.SaveData.HeroMgrComp;

    public int[] HeroIds => Comp.Heroes.Select(hero => hero.HeroId).ToArray();
    public int[] AvailableHeroIds => HeroIds.Where(id => !Comp.BannedHeroes.ContainsKey(id) && !Comp.CineLockedHeroes.Contains(id)).ToArray();
    public int[] CineLocked => Comp.CineLockedHeroes.ToArray();

    public HeroState? Find(int heroId) => Comp.Heroes.FirstOrDefault(hero => hero.HeroId == heroId);

    public bool ValidateTeam(int[] heroes, int fatigue = 0, int min = 1, int max = 3)
    {
        return heroes.Length >= min && heroes.Length <= max && heroes.Distinct().Count() == heroes.Length &&
            heroes.All(id => Find(id) is { } hero && hero.Fatigue >= fatigue && !Comp.BannedHeroes.ContainsKey(id) && !Comp.CineLockedHeroes.Contains(id));
    }

    public void ConsumeTeamFatigue(int[] heroes, int amount)
    {
        if (amount < 0 || !ValidateTeam(heroes, amount)) throw new InvalidOperationException("神器使队伍或疲劳无效");
        foreach (int hero in heroes) ConsumeFatigue(hero, amount);
        Synchronize();
    }

    public void Synchronize() => Notify("syncHeroesData", new() { ["d"] = ToSnapshot() });

    public void SetBanned(int id, int reason)
    {
        bool wasBanned = Comp.BannedHeroes.ContainsKey(id);
        if (reason == 0) Comp.BannedHeroes.Remove(id);
        else Comp.BannedHeroes[id] = reason;
        if (wasBanned != (reason != 0) && Find(id) is not null)
            Notify(reason == 0 ? "recoverBanHeroes" : "banHeroes", new() { [reason == 0 ? "rhs" : "bhs"] = new[] { id } });
        MarkDirty();
    }

    public void LockCinematic(int[] heroes)
    {
        int[] added = heroes.Where(id => !Comp.CineLockedHeroes.Contains(id)).Distinct().ToArray();
        Comp.CineLockedHeroes.Add(added);
        if (added.Length > 0) Notify("cineLockHeroes", new() { ["h"] = added });
        MarkDirty();
    }

    public Dictionary<int, int> Banned() => Comp.BannedHeroes.ToDictionary(pair => pair.Key, pair => pair.Value);

    public void ResetStoryHeroes()
    {
        Comp.Heroes.Clear();
        Comp.BannedHeroes.Clear();
        Comp.CineLockedHeroes.Clear();
        MarkDirty();
    }

    public object[] ToSnapshot(bool banned = false)
    {
        return Comp.Heroes.Where(hero => Comp.BannedHeroes.ContainsKey(hero.HeroId) == banned).Select(hero => (object)new object[]
        {
            hero.HeroId,
            new Dictionary<string, object?>
            {
                ["sl"] = hero.StarLevel,
                ["so"] = hero.StarOrder,
                ["cf"] = hero.Fatigue,
                ["ef"] = hero.Friendly,
                ["ar"] = new Dictionary<string, object>
                {
                    ["o"] = 1,
                    ["ss"] = 3,
                    ["oa"] = ArtifactAttributes.ToDictionary(attribute => attribute, _ => 0),
                    ["ot"] = ArtifactAttributes.ToDictionary(attribute => attribute, _ => 60),
                    ["rotc"] = ArtifactAttributes.ToDictionary(attribute => attribute, _ => 0),
                    ["sl"] = Array.Empty<object>(),
                },
                ["ti"] = new Dictionary<string, object> { ["mc"] = 25, ["items"] = Array.Empty<object>() },
                ["cat"] = null,
                ["sr"] = CalculateScore(hero),
            },
        }).ToArray();
    }

    public bool Unlock(int heroId)
    {
        if (!GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row))
        {
            throw new ArgumentOutOfRangeException(nameof(heroId), "神器使 ID 不存在");
        }
        if (Comp.Heroes.Any(hero => hero.HeroId == heroId)) return false;

        Comp.Heroes.Add(new HeroState { HeroId = heroId, StarLevel = 1, StarOrder = 1, Fatigue = row.FatigueValue });
        MarkDirty();
        object[] snapshot = ToSnapshot(Comp.BannedHeroes.ContainsKey(heroId)).Cast<object[]>().First(value => (int)value[0] == heroId);
        Notify("addHero", new() { ["h"] = heroId, ["d"] = snapshot[1] });
        if (Comp.BannedHeroes.ContainsKey(heroId)) Notify("banHeroes", new() { ["bhs"] = new[] { heroId } });
        return true;
    }

    public int UnlockAll()
    {
        int count = 0;
        foreach (HeroData row in GameTableCatalog.Instance.GetAllData<HeroData>())
        {
            if (Unlock(row.ProtoId)) count++;
        }
        return count;
    }

    public bool IncStarOrder(int heroId)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is null || hero.StarLevel is < 1 or > 4 || hero.StarOrder is < 1 or > 4 ||
            (hero.StarLevel == 4 && hero.StarOrder == 4)) return false;

        if (hero.StarOrder == 4)
        {
            hero.StarLevel++;
            hero.StarOrder = 1;
        }
        else
        {
            hero.StarOrder++;
        }

        MarkDirty();
        return true;
    }

    public int GetInsightValue(int heroId)
    {
        HeroState hero = Comp.Heroes.First(entry => entry.HeroId == heroId);
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row);
        return (row?.InsightValue ?? 0) + (GetStarSkill(hero)?.InsightBonus ?? 0);
    }

    public int GetConstructValue(int heroId)
    {
        HeroState hero = Comp.Heroes.First(entry => entry.HeroId == heroId);
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row);
        return (row?.ConstructValue ?? 0) + (GetStarSkill(hero)?.ConstructBonus ?? 0);
    }

    public int GetLeadershipValue(int heroId)
    {
        HeroState hero = Comp.Heroes.First(entry => entry.HeroId == heroId);
        GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row);
        return (row?.LeadershipValue ?? 0) + (GetStarSkill(hero)?.LeadershipBonus ?? 0);
    }

    private static HeroStarSkillData? GetStarSkill(HeroState hero) =>
        GameTableCatalog.Instance.TryGetDataById<HeroStarSkillData>(hero.HeroId * 100 + hero.StarLevel * 10 + hero.StarOrder, out HeroStarSkillData? row) ? row : null;

    public bool ConsumeFatigue(int heroId, int amount)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (amount < 0 || hero is null || hero.Fatigue < amount) return false;
        hero.Fatigue -= amount;
        MarkDirty();
        return true;
    }

    public void AddFriendly(int heroId, int amount)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is not null)
        {
            hero.Friendly = Math.Clamp(hero.Friendly + amount, 0, 100);
            MarkDirty();
        }
    }

    public void RestoreAllFatigue(int amount)
    {
        foreach (HeroState hero in Comp.Heroes)
        {
            int max = GameTableCatalog.Instance.GetDataById<HeroData>(hero.HeroId)?.FatigueValue ?? 100;
            hero.Fatigue = Math.Clamp(hero.Fatigue + amount, 0, max);
        }
        MarkDirty();
        Synchronize();
    }

    public void AddFatigue(int heroId, int amount)
    {
        if (heroId == 0) { RestoreAllFatigue(amount); return; }
        if (Find(heroId) is not { } hero) return;
        int max = GameTableCatalog.Instance.GetDataById<HeroData>(heroId)?.FatigueValue ?? 100;
        hero.Fatigue = Math.Clamp(hero.Fatigue + amount, 0, max);
        MarkDirty();
    }

    private int CalculateScore(HeroState hero)
    {
        int level = Player.Profile.Level;
        int star = (hero.StarLevel - 1) * 4 + hero.StarOrder;
        return (int)(level * 100 + level * 100 * 1.74 * star / 16);
    }
}
