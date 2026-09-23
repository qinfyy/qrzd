using Sv.Database;
using Sv.Game;
using Sv.Gateway;

namespace Sv.GameMaster;

public sealed class CommandContext
{
    private readonly List<string> _args = [];
    private readonly List<string> _messages = [];
    private Player? _target;

    public CommandContext(string commandLine, GatewayHostedService gateway, Player? sender = null)
    {
        if (string.IsNullOrWhiteSpace(commandLine) || commandLine.Length > 1024)
        {
            throw new GameMasterCommandException("命令不能为空且不能超过 1024 字符");
        }

        Gateway = gateway;
        Sender = sender;
        Raw = commandLine.Trim();
        string[] tokens = Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Label = tokens[0].TrimStart('/', '!').ToLowerInvariant();
        if (Label.Length == 0) throw new GameMasterCommandException("命令不能为空");

        TargetUid = sender?.Uid ?? 0;
        _target = sender;
        for (int i = 1; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token.Length > 1 && token[0] == '@' && long.TryParse(token.AsSpan(1), out long uid))
            {
                if (uid <= 0) throw new GameMasterCommandException("@uid 必须是正整数");
                if (sender is not null && uid != sender.Uid) throw new GameMasterCommandException("游戏内 GM 只能修改自己的角色");
                if (sender is null && TargetUid > 0 && TargetUid != uid) throw new GameMasterCommandException("只能指定一个目标玩家");
                TargetUid = uid;
                continue;
            }
            _args.Add(token);
        }
    }

    public string Label { get; }

    public string Raw { get; }

    public IReadOnlyList<string> Args => _args;

    public IReadOnlyList<string> Messages => _messages;

    public Player? Sender { get; }

    public long TargetUid { get; private set; }

    public GatewayHostedService Gateway { get; }

    public GatewaySession? TargetSession => TargetUid > 0 ? Gateway.GetSessionByUid(TargetUid) : null;

    public Player GetTargetPlayer()
    {
        if (_target is not null) return _target;
        long uid = RequireUid();
        _target = TargetSession?.Player ?? GameDatabase.Instance.GetByUid(uid) ?? throw new GameMasterCommandException($"玩家 {uid} 不存在");
        return _target;
    }

    public long RequireUid() => TargetUid > 0 ? TargetUid : throw new GameMasterCommandException("请指定 @uid；游戏内发送命令时默认作用于自己");

    public string? GetArg(int index) => index >= 0 && index < _args.Count ? _args[index] : null;

    public void RequireArgCount(int count)
    {
        if (_args.Count != count) throw new GameMasterCommandException("参数数量不正确，使用 /help 查看用法");
    }

    public int RequireNonNegativeInt(int index, string usage)
    {
        if (GetArg(index) is not { } text || !int.TryParse(text, out int value) || value < 0)
        {
            throw new GameMasterCommandException($"参数必须为非负整数：{usage}");
        }
        return value;
    }

    public int RequirePositiveInt(int index, string usage)
    {
        int value = RequireNonNegativeInt(index, usage);
        if (value == 0) throw new GameMasterCommandException($"参数必须为正整数：{usage}");
        return value;
    }

    public void SendMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        _messages.Add(message);
        Sender?.Chat.EchoFromServer(message);
    }

    public void SendMessages(IEnumerable<string> messages)
    {
        foreach (string message in messages) SendMessage(message);
    }
}
