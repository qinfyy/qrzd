using Sv.Database;

namespace Sv.Game;

public sealed class StatusLogic(Player player) : PlayerLogicBase(player)
{
    private StatusComp Comp => Player.SaveData.StatusComp;

    public bool Emergency => Comp.Emergency;

    public void SetEmergency(bool emergency)
    {
        if (Comp.Emergency == emergency) return;
        Comp.Emergency = emergency;
        MarkDirty();
    }

    public string Check(string status)
    {
        if (status == "nextday") return Player.WeekNum.CanAdvance();
        if (status == "city") return "ok";
        if (Player.Combat.IsActive) return "battle";
        if (Player.WeekNum.EndingId != 0) return "ending";
        if (Emergency) return "event";
        if (status is "bbs" or "intellgence") return "ok";
        if (status == "summon") return Player.Newbee.CanSummon ? "ok" : "unsupported";
        if (status.StartsWith("area", StringComparison.Ordinal) && int.TryParse(status.AsSpan(4), out int area))
            return Player.City.FindArea(area) is { Status: 0, Lock: false } ? "ok" : "locked";
        return "unsupported";
    }

    public string Switch(string status)
    {
        string result = Check(status);
        if (result == "ok") CurrentStatus = status;
        return result;
    }

    public Dictionary<string, object> ToSnapshot() => new() { ["c"] = CurrentStatus };

    public void Reset()
    {
        CurrentStatus = "city";
        SetEmergency(true);
    }

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
