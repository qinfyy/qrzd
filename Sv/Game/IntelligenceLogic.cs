using Sv.Database;

namespace Sv.Game;

public sealed class IntelligenceLogic(Player player) : PlayerLogicBase(player)
{
    private IntelligenceComp Comp => Player.SaveData.IntelligenceComp;

    public bool Readed
    {
        get => Comp.Readed;
        set
        {
            if (Comp.Readed != value)
            {
                Comp.Readed = value;
                MarkDirty();
            }
        }
    }
}
