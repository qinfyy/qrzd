using Sv.Database;

namespace Sv.Game;

public sealed class SocialLogic(Player player) : PlayerLogicBase(player)
{
    private SocialComp Comp => Player.SaveData.SocialComp;
}
