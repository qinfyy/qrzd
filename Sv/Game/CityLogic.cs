using Sv.Database;

namespace Sv.Game;

public sealed class CityLogic(Player player) : PlayerLogicBase(player)
{
    private CityComp Comp => Player.SaveData.CityComp;

    public int ActionVal
    {
        get => Comp.ActionVal;
        set
        {
            if (Comp.ActionVal != value)
            {
                Comp.ActionVal = value;
                MarkDirty();
            }
        }
    }

    public int DevelopVal
    {
        get => Comp.DevelopVal;
        set
        {
            if (Comp.DevelopVal != value)
            {
                Comp.DevelopVal = value;
                MarkDirty();
            }
        }
    }

    public int DevelopValCount
    {
        get => Comp.DevelopValCount;
        set
        {
            if (Comp.DevelopValCount != value)
            {
                Comp.DevelopValCount = value;
                MarkDirty();
            }
        }
    }

    public int BuildFund
    {
        get => Comp.BuildFund;
        set
        {
            if (Comp.BuildFund != value)
            {
                Comp.BuildFund = value;
                MarkDirty();
            }
        }
    }

    public int FatigueVal
    {
        get => Comp.FatigueVal;
        set
        {
            if (Comp.FatigueVal != value)
            {
                Comp.FatigueVal = value;
                MarkDirty();
            }
        }
    }

    public int EventVal
    {
        get => Comp.EventVal;
        set
        {
            if (Comp.EventVal != value)
            {
                Comp.EventVal = value;
                MarkDirty();
            }
        }
    }

    public int ResearchVal
    {
        get => Comp.ResearchVal;
        set
        {
            if (Comp.ResearchVal != value)
            {
                Comp.ResearchVal = value;
                MarkDirty();
            }
        }
    }
}
