using System.IO.Compression;
using Google.Protobuf;
using MessagePack;
using MongoDB.Bson;
using Sv.Configuration;
using Sv.Database;
using Sv.Gateway;

namespace Sv.Game;

/// <summary>
/// 玩家聚合根。按值持有 <see cref="PlayerSaveData"/>（存储态即 SQLite players.data），
/// 独占构造并驱动全部领域 Manager，持有唯一的状态锁 <see cref="SyncRoot"/>。
/// 包含领域级快照生成 ToAvatarSnapshot，不依赖外部辅助类。
/// </summary>
public sealed class Player
{
    private readonly List<PlayerLogicBase> _logics = [];

    public Player(long uid, PlayerSaveData saveData)
    {
        Uid = uid;
        InitialPlayerTemplate.EnsureComps(saveData);
        SaveData = saveData;

        Profile = Add(new PlayerProfileLogic(this));
        City = Add(new CityLogic(this));
        WeekNum = Add(new WeekNumLogic(this));
        Status = Add(new StatusLogic(this));
        Inventory = Add(new InventoryLogic(this));
        HeroMgr = Add(new HeroMgrLogic(this));
        Social = Add(new SocialLogic(this));
        Chat = Add(new ChatLogic(this));
        Intelligence = Add(new IntelligenceLogic(this));
        Story = Add(new StoryLogic(this));
        EventTrigger = Add(new EventTriggerLogic(this));
        Newbee = Add(new NewbeeLogic(this));
        Combat = Add(new CombatLogic(this));
        Rpc = Add(new RpcLogic(this));
    }

    public long Uid { get; }

    public PlayerSaveData SaveData { get; private set; }

    public PlayerProfileLogic Profile { get; }

    public CityLogic City { get; }

    public WeekNumLogic WeekNum { get; }

    public StatusLogic Status { get; }

    public InventoryLogic Inventory { get; }

    public HeroMgrLogic HeroMgr { get; }

    public SocialLogic Social { get; }

    public ChatLogic Chat { get; }

    public IntelligenceLogic Intelligence { get; }

    public StoryLogic Story { get; }
    public EventTriggerLogic EventTrigger { get; }
    public NewbeeLogic Newbee { get; }
    public CombatLogic Combat { get; }
    public RpcLogic Rpc { get; }
    public event Action<string, Dictionary<string, object>>? Notification;

    public bool IsDirty { get; private set; }

    public object SyncRoot { get; } = new();

    public GatewaySession? Session { get; internal set; }

    public static Player CreateNew(long uid, string nickName = "Cyt", int serverId = 5004)
    {
        PlayerSaveData saveData = new();
        string avatarId = ObjectId.GenerateNewId().ToString();
        InitialPlayerTemplate.ApplyTo(saveData, uid, nickName, serverId, avatarId);
        Player player = new(uid, saveData);
        lock (player.SyncRoot)
        {
            player.OnCreate();
        }
        return player;
    }

    public static Player FromBlob(long uid, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        PlayerSaveData saveData = PlayerSaveData.Parser.ParseFrom(blob);
        Player player = new(uid, saveData);
        lock (player.SyncRoot) player.OnLoad();
        return player;
    }

    public byte[] SaveToBlob() => SaveData.ToByteArray();

    public void Save() => GameDatabase.Instance.Save(this);

    public void OnCreate()
    {
        IsDirty = true;
        foreach (PlayerLogicBase logic in _logics)
        {
            logic.OnCreate();
        }
    }

    public void OnLoad()
    {
        IsDirty = false;
        foreach (PlayerLogicBase logic in _logics)
        {
            logic.OnLoad();
        }
    }

    public void OnLogin()
    {
        foreach (PlayerLogicBase logic in _logics)
        {
            logic.OnLogin();
        }
    }

    public void BeforeSave()
    {
        foreach (PlayerLogicBase logic in _logics)
        {
            logic.BeforeSave();
        }
    }

    public void Trigger(PlayerEventType eventType, int param1 = 0, int param2 = 0, int param3 = 0, object? state = null)
    {
        foreach (PlayerLogicBase logic in _logics)
        {
            logic.OnPlayerEvent(eventType, param1, param2, param3, state);
        }
    }

    /// <summary>
    /// 生成客户端初始快照并压缩，作为领域对象核心能力。
    /// </summary>
    public byte[] ToAvatarSnapshot(ServerOptions options)
    {
        Dictionary<string, object> snapshot = new()
        {
            ["st"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["an"] = Profile.NickName,
            ["name"] = Profile.NickName,
            ["uid"] = Uid,
            ["sv"] = Profile.ServerId,
            ["hn"] = options.HostId,
            ["role"] = Profile.RoleId,
            ["level"] = Profile.Level,
            ["money"] = Profile.Money,
            ["crystal"] = Profile.Crystal,
            ["exp"] = Profile.Experience,
            ["summonCoin"] = Profile.SummonCoin,
            ["cgs"] = Story.Cgs.ToArray(),
            ["ccgs"] = Story.CurrentCgs,
            ["weeknum"] = WeekNum.ToSnapshot(),
            ["status"] = Status.ToSnapshot(),
            ["city"] = City.ToCityDataSnapshot(),
            ["std"] = Story.ToSnapshot(),
            ["eventTrigger"] = EventTrigger.ToSnapshot(),
            ["nbd"] = Newbee.ToSnapshot(),
            ["combat"] = Combat.ToSnapshot(),
            ["bs"] = EventTrigger.ToBattleSnapshot(),
            ["ldd"] = Newbee.ToSummonSnapshot(),
            ["reward"] = new Dictionary<string, object>
            {
                ["bc"] = City.Blackcores().Where(pair => pair.Value != 0).Select(pair => (object)new[] { pair.Key, pair.Value }).ToArray(),
            },
            ["inv"] = new Dictionary<string, object>
            {
                ["mc"] = Inventory.MaxCost,
                ["items"] = Inventory.ToSnapshot(),
            },
            ["heromgr"] = new Dictionary<string, object>
            {
                ["hrs"] = HeroMgr.ToSnapshot(),
                ["bhrs"] = HeroMgr.ToSnapshot(true),
                ["c"] = HeroMgr.CineLocked,
                ["cfs"] = new Dictionary<string, object>(),
            },
            ["sd"] = Social.ToSnapshot(),
            ["intelligence"] = Intelligence.ToSnapshot(),
        };

        byte[] message = MessagePackSerializer.Serialize(snapshot);
        using MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(message);
        }
        return output.ToArray();
    }

    internal void MarkDirty() => IsDirty = true;

    internal void MarkSaved() => IsDirty = false;

    internal void Restore(byte[] blob)
    {
        if (!Monitor.IsEntered(SyncRoot)) throw new InvalidOperationException("恢复存档必须持有玩家锁");
        SaveData = PlayerSaveData.Parser.ParseFrom(blob);
        OnLoad();
    }

    internal void Notify(string method, Dictionary<string, object> arguments) => Notification?.Invoke(method, arguments);

    private T Add<T>(T logic) where T : PlayerLogicBase
    {
        _logics.Add(logic);
        return logic;
    }
}
