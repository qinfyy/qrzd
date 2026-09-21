using Sv.Database;

namespace Sv.Game;

public sealed class StatusLogic(Player player) : PlayerLogicBase(player)
{
    private StatusComp Comp => Player.SaveData.StatusComp;

    public string CurrentStatus
    {
        get => Comp.CurrentStatus;
        set
        {
            if (Comp.CurrentStatus != value)
            {
                Comp.CurrentStatus = value;
                MarkDirty();
            }
        }
    }
}
