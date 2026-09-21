using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog;
using Sv.Configuration;
using Sv.Game;
using Sv.Utility;

namespace Sv.Database;

public sealed partial class GameDatabase
{
    private static GameDatabase? _instance;

    /// <summary>全局唯一游戏数据库单例；初始化前访问属于启动时序错误，直接抛出。</summary>
    public static GameDatabase Instance =>
        _instance ?? throw new InvalidOperationException("GameDatabase 尚未初始化，请确保服务器启动前已调用 GameDatabase.Initialize()");

    private readonly ILogger _logger = Log.ForContext<GameDatabase>();
    private readonly DbContextOptions<GameDbContext> _dbContextOptions;
    private readonly Lock _writeLock = new();

    private GameDatabase(DbContextOptions<GameDbContext> dbContextOptions)
    {
        _dbContextOptions = dbContextOptions;
    }

    /// <summary>内部 DbContext 工厂：生命周期只由本类型的实例方法管理，对领域层与协议层封闭。</summary>
    private GameDbContext CreateDbContext() => new(_dbContextOptions);

    /// <summary>入口点显式初始化：路径创建、连接配置、物理建表与 SQLite PRAGMA 设置。</summary>
    public static void Initialize(DatabaseOptions options)
    {
        string? directory = Path.GetDirectoryName(options.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = options.Path,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = true,
        };
        DbContextOptionsBuilder<GameDbContext> builder = new();
        builder.UseSqlite(connectionString.ConnectionString);

        GameDatabase database = new(builder.Options);
        using (GameDbContext db = database.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Database.OpenConnection();
            try
            {
                db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
                db.Database.ExecuteSqlRaw("INSERT OR IGNORE INTO sqlite_sequence (name, seq) VALUES ('Players', 10000);");
            }
            finally
            {
                db.Database.CloseConnection();
            }
        }

        _instance = database;
        database._logger.Information("GameDatabase 已初始化，路径 {Path}", options.Path);
    }

    /// <summary>
    /// 按用户名/指挥使名称与区服获取或创建玩家角色。
    /// </summary>
    public Player GetOrCreateByName(string name, int serverId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PlayerIdentityException("登录请求缺少名称");
        }

        lock (_writeLock)
        {
            using GameDbContext db = CreateDbContext();
            PlayerEntity? entity = db.Players.SingleOrDefault(value =>
                value.Name == name && value.ServerId == serverId);
            if (entity is not null)
            {
                return Player.FromBlob(entity.Uid, entity.Data);
            }

            using IDbContextTransaction transaction = db.Database.BeginTransaction();
            Player player = InsertNewPlayer(db, name, serverId);
            transaction.Commit();
            player.MarkSaved();
            return player;
        }
    }

    public Player? GetByUid(long uid)
    {
        if (uid <= 0)
        {
            throw new PlayerIdentityException("玩家 UID 无效");
        }

        using GameDbContext db = CreateDbContext();
        PlayerEntity? entity = db.Players.SingleOrDefault(value => value.Uid == uid);
        if (entity is null)
        {
            return null;
        }

        return Player.FromBlob(entity.Uid, entity.Data);
    }

    public void Save(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (player.SyncRoot)
        {
            if (!player.IsDirty)
            {
                return;
            }

            lock (_writeLock)
            {
                using GameDbContext db = CreateDbContext();
                PlayerEntity entity = db.Players.SingleOrDefault(value => value.Uid == player.Uid) ?? throw new InvalidDataException($"玩家 {player.Uid} 不存在，无法保存");
                player.BeforeSave();
                entity.Data = player.SaveToBlob();
                entity.UpdatedAt = Time.Now;
                db.SaveChanges();
                player.MarkSaved();
            }
        }
    }

    private static Player InsertNewPlayer(GameDbContext db, string name, int serverId)
    {
        DateTimeOffset now = Time.Now;
        PlayerEntity entity = new()
        {
            Name = name,
            ServerId = serverId,
            CreatedAt = now,
            UpdatedAt = now,
            Data = [],
        };
        db.Players.Add(entity);
        db.SaveChanges();
        if (entity.Uid <= 0)
        {
            throw new InvalidDataException("SQLite 未生成有效玩家 UID");
        }

        Player player = Player.CreateNew(entity.Uid, name, serverId);
        entity.Data = player.SaveToBlob();
        db.SaveChanges();
        return player;
    }
}

public sealed class PlayerIdentityException(string message) : Exception(message);
