using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class PlayerProfileLogic(Player player) : PlayerLogicBase(player)
{
    public static IReadOnlyList<int> CurrencyItemIds { get; } = [102, 89, 90];

    private PlayerProfileComp Comp => Player.SaveData.ProfileComp;

    public int GetCurrency(int itemId) => itemId switch
    {
        102 => Money,
        89 => Crystal,
        90 => SummonCoin,
        _ => throw new ArgumentOutOfRangeException(nameof(itemId), "当前支持金币 102、晶尘 89、欧泊 90；其他虚拟货币尚未实现"),
    };

    public void GrantCurrency(int itemId, int count)
    {
        if (count <= 0 || (long)GetCurrency(itemId) + count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(count), "货币数量必须为正整数，且余额不能超过 2147483647");
        switch (itemId)
        {
            case 102: AddMoney(count); break;
            case 89: AddCrystal(count); break;
            case 90: AddSummonCoin(count); break;
        }
    }

    public void GrantAllCurrencies(int count)
    {
        if (count <= 0 || CurrencyItemIds.Any(id => (long)GetCurrency(id) + count > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(count), "货币数量无效或余额溢出，未发放任何货币");
        foreach (int itemId in CurrencyItemIds) GrantCurrency(itemId, count);
    }

    public void SetPlayerName(string name, bool isNewName)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 8 || name.Any(char.IsControl))
            throw new InvalidOperationException("名字须为 1 到 8 个字，且不能包含控制字符");
        if (Player.WeekNum.Week != 0 || Player.WeekNum.Day != 0 || Player.EventTrigger.Find(1000) is null && Player.EventTrigger.Find(15) is null)
            throw new InvalidOperationException("当前不在新手起名剧情中");
        NickName = name;
        Notify("setPlayerNameReply", new() { ["r"] = true, ["in"] = isNewName });
    }

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
        if ((long)Crystal + amount is < 0 or > int.MaxValue) throw new InvalidOperationException("晶尘数量无效");
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
