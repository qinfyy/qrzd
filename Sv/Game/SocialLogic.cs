using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class SocialLogic(Player player) : PlayerLogicBase(player)
{
    private SocialComp Comp => Player.SaveData.SocialComp;

    public bool CanAddMessage(int id, bool personal) => personal
        ? GameTableCatalog.Instance.GetDataById<PrivateMessageData>(id) is not null : GameTableCatalog.Instance.GetDataById<TerminalMessageData>(id) is not null;

    public void AddMessage(int id, bool personal)
    {
        if (!CanAddMessage(id, personal)) throw new InvalidOperationException($"剧情消息不存在: {id}");
        var messages = personal ? Comp.PrivateMessages : Comp.TerminalMessages;
        if (messages.Any(message => message.Id == id)) return;
        StoryMessage message = new() { Id = id, ReceivedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        messages.Add(message);
        Sync(message, personal);
        MarkDirty();
    }

    private Dictionary<string, object> MessageSnapshot(StoryMessage message, bool personal)
    {
        if (personal) return new() { ["id"] = message.Id, ["rd"] = message.Read, ["rp"] = message.SelectedOption, ["rt"] = message.ReceivedAt };
        return new()
        {
            ["id"] = message.Id, ["r"] = message.Read, ["rt"] = message.ReceivedAt, ["ct"] = 0, ["l"] = Array.Empty<object>(), ["a"] = false,
            ["e"] = message.Complete ? 0 : GameTableCatalog.Instance.GetDataById<TerminalMessageData>(message.Id)!.Int("event"),
        };
    }

    private void Sync(StoryMessage message, bool personal) => Notify(personal ? "syncOnePrivateMsg" : "syncOneSocialMsg", new()
    {
        ["id"] = message.Id, ["c"] = MessageSnapshot(message, personal),
    });

    public Dictionary<string, object> ToSnapshot() => new()
    {
        ["sm"] = Comp.TerminalMessages.ToDictionary(message => message.Id.ToString(), message => (object)MessageSnapshot(message, false)),
        ["pm"] = Comp.PrivateMessages.ToDictionary(message => message.Id.ToString(), message => (object)MessageSnapshot(message, true)),
        ["epm"] = new Dictionary<string, object>(), ["fe"] = Array.Empty<object>(),
    };

    public bool Read(int id, bool personal)
    {
        StoryMessage? message = (personal ? Comp.PrivateMessages : Comp.TerminalMessages).FirstOrDefault(value => value.Id == id);
        if (message is null) return false;
        message.Read = true;
        Sync(message, personal);
        MarkDirty();
        return true;
    }

    public bool Reply(int id, int option)
    {
        StoryMessage? message = Comp.PrivateMessages.FirstOrDefault(value => value.Id == id);
        PrivateMessageData? row = GameTableCatalog.Instance.GetDataById<PrivateMessageData>(id);
        if (message is null || row is null || option <= 0 || !row.Has($"branchContent{option}")) return false;
        if (message.Complete) return message.SelectedOption == option;
        int eventId = row.Int($"branchReward{option}");
        if (eventId != 0 && !Player.EventTrigger.CanQueue(eventId)) return false;
        message.Read = message.Complete = true;
        message.SelectedOption = option;
        if (eventId != 0) Player.EventTrigger.Queue(eventId);
        Sync(message, true);
        MarkDirty();
        return true;
    }

    public bool Trigger(int id)
    {
        StoryMessage? message = Comp.TerminalMessages.FirstOrDefault(value => value.Id == id);
        TerminalMessageData? row = GameTableCatalog.Instance.GetDataById<TerminalMessageData>(id);
        if (message is null || row is null || message.Complete || !row.Ints("require_events").All(Player.EventTrigger.IsCompleted)) return false;
        int eventId = row.Int("event");
        if (!Player.EventTrigger.CanQueue(eventId)) return false;
        message.Read = message.Complete = true;
        Player.EventTrigger.Queue(eventId);
        Sync(message, false);
        MarkDirty();
        return Player.EventTrigger.Start(eventId);
    }

    public void Reset()
    {
        Comp.PrivateMessages.Clear();
        Comp.TerminalMessages.Clear();
        MarkDirty();
    }
}
