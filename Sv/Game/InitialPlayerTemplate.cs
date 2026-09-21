using Sv.Database;

namespace Sv.Game;

public static class InitialPlayerTemplate
{
    public static void EnsureComps(PlayerSaveData saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        saveData.ProfileComp ??= new PlayerProfileComp();
        saveData.WeekNumComp ??= new WeekNumComp();
        saveData.StatusComp ??= new StatusComp();
        saveData.CityComp ??= new CityComp();
        saveData.InventoryComp ??= new InventoryComp();
        saveData.HeroMgrComp ??= new HeroMgrComp();
        saveData.SocialComp ??= new SocialComp();
        saveData.IntelligenceComp ??= new IntelligenceComp();
    }

    public static void ApplyTo(PlayerSaveData saveData, long uid, string nickName, int serverId, string avatarId)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        EnsureComps(saveData);

        saveData.ProfileComp.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        saveData.ProfileComp.NickName = string.IsNullOrWhiteSpace(nickName) ? "指挥使" : nickName;
        saveData.ProfileComp.Level = 1;
        saveData.ProfileComp.RoleId = 1;
        saveData.ProfileComp.AvatarId = avatarId;
        saveData.ProfileComp.ServerId = serverId;

        saveData.WeekNumComp.Week = 1;
        saveData.WeekNumComp.Day = 0;

        saveData.StatusComp.CurrentStatus = "city";

        saveData.CityComp.ActionVal = 24;
        saveData.CityComp.DevelopVal = 0;
        saveData.CityComp.DevelopValCount = 0;
        saveData.CityComp.BuildFund = 0;
        saveData.CityComp.FatigueVal = 0;
        saveData.CityComp.EventVal = 0;
        saveData.CityComp.ResearchVal = 0;

        saveData.InventoryComp.MaxCost = 100;

        saveData.IntelligenceComp.Readed = false;
    }
}
