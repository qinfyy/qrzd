using Sv.Database;

namespace Sv.Game;

public sealed class HeroMgrLogic(Player player) : PlayerLogicBase(player)
{
    private HeroMgrComp Comp => Player.SaveData.HeroMgrComp;
}
