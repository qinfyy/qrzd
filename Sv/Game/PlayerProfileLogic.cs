using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class PlayerProfileLogic(Player player) : PlayerLogicBase(player)
{
    private PlayerProfileComp Comp => Player.SaveData.ProfileComp;

    public string NickName
    {
        get => Comp.NickName;
        set
        {
            if (Comp.NickName != value)
            {
                Comp.NickName = value;
                MarkDirty();
            }
        }
    }

    public int Level
    {
        get => Comp.Level;
        set
        {
            if (Comp.Level != value)
            {
                Comp.Level = value;
                MarkDirty();
            }
        }
    }

    public int RoleId
    {
        get => Comp.RoleId;
        set
        {
            if (Comp.RoleId != value)
            {
                Comp.RoleId = value;
                MarkDirty();
            }
        }
    }

    public string AvatarId
    {
        get => Comp.AvatarId;
        set
        {
            if (Comp.AvatarId != value)
            {
                Comp.AvatarId = value;
                MarkDirty();
            }
        }
    }

    public int ServerId
    {
        get => Comp.ServerId;
        set
        {
            if (Comp.ServerId != value)
            {
                Comp.ServerId = value;
                MarkDirty();
            }
        }
    }

    public long CreateTime => Comp.CreateTime;

    public int Experience => Comp.Experience;
    public int SummonCoin => Comp.SummonCoin;

    public void AddSummonCoin(int amount)
    {
        if ((long)SummonCoin + amount is < 0 or > int.MaxValue) throw new InvalidOperationException("欧泊数量无效");
        Comp.SummonCoin += amount;
        Notify("updateSummonCoin", new() { ["v"] = SummonCoin });
        MarkDirty();
    }

    public Dictionary<string, object> AddExperience(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int oldLevel = Level;
        int oldExperience = Experience;
        Comp.Experience = checked(Experience + amount);
        while (GameTableCatalog.Instance.TryGetDataById<PlayerExperienceData>(Level, out var data) && data.Int("lv_exp") > 0 &&
            Experience >= data.Int("lv_exp") && GameTableCatalog.Instance.TryGetDataById<PlayerExperienceData>(Level + 1, out _))
        {
            Comp.Experience -= data.Int("lv_exp");
            Level++;
        }
        MarkDirty();
        Notify("updateExpAndLevel", new() { ["e"] = Experience, ["lv"] = Level });
        return new() { ["exp"] = amount, ["ol"] = oldLevel, ["oe"] = oldExperience, ["cl"] = Level, ["ce"] = Experience };
    }

    public void AddMoney(int amount)
    {
        if ((long)Money + amount is < 0 or > int.MaxValue) throw new InvalidOperationException("金币数量无效");
        Money += amount;
        Notify("updateMoney", new() { ["v"] = Money });
    }

    public void AddCrystal(int amount)
    {
        if ((long)Crystal + amount is < 0 or > int.MaxValue) throw new InvalidOperationException("晶钻数量无效");
        Crystal += amount;
        Notify("updateCrystal", new() { ["v"] = Crystal, ["bv"] = 0 });
    }

    public int Money
    {
        get => Comp.Money;
        set
        {
            if (Comp.Money == value) return;
            Comp.Money = value;
            MarkDirty();
        }
    }

    public int Crystal
    {
        get => Comp.Crystal;
        set
        {
            if (Comp.Crystal == value) return;
            Comp.Crystal = value;
            MarkDirty();
        }
    }
}
