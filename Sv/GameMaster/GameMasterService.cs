using Sv.Game;
using Sv.Gateway;

namespace Sv.GameMaster;

public sealed class GameMasterCommandException(string message) : Exception(message);

public sealed class GameMasterService
{
    public GatewayHostedService Gateway { get; }

    public GameMasterService(GatewayHostedService gateway)
    {
        Gateway = gateway;
        gateway.GameMasterService = this;
    }

    public IReadOnlyList<string> Execute(string commandLine, Player? sender = null)
    {
        CommandContext ctx = new(commandLine, Gateway, sender);
        GameMasterCommand command = GameMasterCommandRegistry.Find(ctx.Label)
            ?? throw new GameMasterCommandException($"未知 GM 命令: {ctx.Label}；可用命令见 /help");

        if (command.RequireTarget)
        {
            long uid = ctx.RequireUid();
            if (command.RequireTargetOnline && !Gateway.IsOnline(uid))
            {
                throw new GameMasterCommandException($"目标玩家 {uid} 不在线");
            }
        }

        command.Execute(ctx);
        if (ctx.Messages.Count == 0) ctx.SendMessage("执行成功");
        return ctx.Messages;
    }
}
