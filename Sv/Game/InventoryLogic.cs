using Sv.Database;

namespace Sv.Game;

public sealed class InventoryLogic(Player player) : PlayerLogicBase(player)
{
    private InventoryComp Comp => Player.SaveData.InventoryComp;

    public int MaxCost
    {
        get => Comp.MaxCost;
        set
        {
            if (Comp.MaxCost != value)
            {
                Comp.MaxCost = value;
                MarkDirty();
            }
        }
    }
}
