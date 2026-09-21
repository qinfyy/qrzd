namespace Sv.Game;

public enum PlayerEventType
{
    /// <summary>
    /// 关卡通过/前置响应阶段（在 66 响应包前触发：图鉴、任务、角色潜能等前置状态更新与推包）。
    /// 参数约定：param1: levelId, param2: passFlag, param3: serverExp。
    /// </summary>
    LevelPassed = 1,

    /// <summary>
    /// 关卡结算完成/后置响应阶段（在 66 响应包后触发：战斗次数、新主线章节与评分、剧情书等后置状态更新与推包）。
    /// 参数约定：param1: levelId, param2: passFlag。
    /// </summary>
    LevelSettled = 2,
}
