using Sv.Database;

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
}
