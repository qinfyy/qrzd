using Sv.Database;

namespace Sv.Game;

public sealed class WeekNumLogic(Player player) : PlayerLogicBase(player)
{
    private WeekNumComp Comp => Player.SaveData.WeekNumComp;

    public int Week
    {
        get => Comp.Week;
        set
        {
            if (Comp.Week != value)
            {
                Comp.Week = value;
                MarkDirty();
            }
        }
    }

    public int Day
    {
        get => Comp.Day;
        set
        {
            if (Comp.Day != value)
            {
                Comp.Day = value;
                MarkDirty();
            }
        }
    }
}
