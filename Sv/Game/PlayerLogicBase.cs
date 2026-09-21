namespace Sv.Game;

/// <summary>
/// 玩家子模块（Logic/Manager）基类。对应客户端/SrvProj 的 ManagerBase：每个有状态
/// Logic 由 Player 持有，同时持有反向 Player 引用，并挂接生命周期钩子。
/// 无状态 Logic 不继承本类，而是保留 static class。
/// </summary>
public abstract class PlayerLogicBase
{
    protected PlayerLogicBase(Player player)
    {
        Player = player;
    }

    public Player Player { get; }

    public void MarkDirty() => Player.MarkDirty();

    protected internal virtual void OnCreate()
    {
    }

    protected internal virtual void OnLoad()
    {
    }

    protected internal virtual void OnLogin()
    {
    }

    protected internal virtual void BeforeSave()
    {
    }

    /// <summary>
    /// 领域事件响应钩子。借鉴 SrvProj 与 Nebula，当 Player.Trigger 广播事件时调用。
    /// 子类根据 eventType 自行处理自身业务并按需下发在线同步包。
    /// </summary>
    protected internal virtual void OnPlayerEvent(PlayerEventType eventType, int param1, int param2, int param3, object? state)
    {
    }
}
