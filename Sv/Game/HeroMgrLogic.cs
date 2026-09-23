using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class HeroMgrLogic(Player player) : PlayerLogicBase(player)
{
    private static readonly int[] StarterHeroIds = [24, 20, 10];
    private static readonly string[] ArtifactAttributes = ["_base_max_hp", "_base_phy_att_str", "_base_mag_att_str", "_base_phy_def", "_base_mag_def"];

    private HeroMgrComp Comp => Player.SaveData.HeroMgrComp;

    public int[] HeroIds => Comp.Heroes.Select(hero => hero.HeroId).ToArray();

    protected internal override void OnCreate()
    {
        foreach (int heroId in StarterHeroIds)
        {
            if (Comp.Heroes.Any(hero => hero.HeroId == heroId)) continue;

            int fatigue = 100;
            if (GameTableCatalog.Instance.TryGetDataById<HeroData>(heroId, out HeroData? row))
            {
                fatigue = row.FatigueValue;
            }

            Comp.Heroes.Add(new HeroState { HeroId = heroId, StarLevel = 1, StarOrder = 1, Fatigue = fatigue });
            MarkDirty();
        }
    }

    public object[] ToSnapshot()
    {
        return Comp.Heroes.Select(hero => (object)new object[]
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
        if (hero is null || hero.Fatigue < amount) return false;
        hero.Fatigue -= amount;
        MarkDirty();
        return true;
    }

    public void AddFriendly(int heroId, int amount)
    {
        HeroState? hero = Comp.Heroes.FirstOrDefault(entry => entry.HeroId == heroId);
        if (hero is not null)
        {
            hero.Friendly = Math.Min(100, hero.Friendly + amount);
            MarkDirty();
        }
    }

    public void RestoreAllFatigue(int amount)
    {
        foreach (HeroState hero in Comp.Heroes)
        {
            hero.Fatigue = Math.Min(100, hero.Fatigue + amount);
        }
        MarkDirty();
    }

    private int CalculateScore(HeroState hero)
    {
        int level = Player.Profile.Level;
        int star = (hero.StarLevel - 1) * 4 + hero.StarOrder;
        return (int)(level * 100 + level * 100 * 1.74 * star / 16);
    }
}
