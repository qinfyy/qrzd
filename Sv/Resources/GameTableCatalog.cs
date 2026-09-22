using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Sv.Resources.Tables;

namespace Sv.Resources;

public sealed class GameTableCatalog
{
    private static GameTableCatalog? _instance;

    public static GameTableCatalog Instance
    {
        get => _instance ?? throw new InvalidOperationException("GameTableCatalog 尚未初始化，请确保资源加载器已执行");
        internal set => _instance = value;
    }

    public bool IsLoaded { get; internal set; }

    // TSV 数据表按 ID 索引：Type -> FrozenDictionary<int, T>
    private readonly Dictionary<Type, object> _loadedTables = [];

    // JSON 复杂配置：Type -> 反序列化对象
    private readonly Dictionary<Type, object> _loadedJsonData = [];

    public int LoadedTableCount => _loadedTables.Count;

    public int LoadedJsonCount => _loadedJsonData.Count;

    public T? GetDataById<T>(int id) where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out object? table) && table is FrozenDictionary<int, T> map)
        {
            return map.GetValueOrDefault(id);
        }

        throw new KeyNotFoundException($"资源表类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    public bool TryGetDataById<T>(int id, [NotNullWhen(true)] out T? data) where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out object? table) && table is FrozenDictionary<int, T> map)
        {
            return map.TryGetValue(id, out data);
        }

        data = null;
        return false;
    }

    /// <summary>获取全量行集合</summary>
    public IReadOnlyList<T> GetAllData<T>() where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out object? table) && table is FrozenDictionary<int, T> map)
        {
            return map.Values is IReadOnlyList<T> list ? list : [.. map.Values];
        }

        throw new KeyNotFoundException($"资源表类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    /// <summary>直接获取整张表的只读字典（用于极少数需要遍历键值对的场景）</summary>
    public FrozenDictionary<int, T> GetTable<T>() where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out object? table) && table is FrozenDictionary<int, T> map)
        {
            return map;
        }

        throw new KeyNotFoundException($"资源表类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    public bool TryGetTable<T>([NotNullWhen(true)] out FrozenDictionary<int, T>? table) where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out object? value) && value is FrozenDictionary<int, T> map)
        {
            table = map;
            return true;
        }

        table = null;
        return false;
    }

    /// <summary>获取 JSON 配置对象（直接返回强类型实体，彻底干掉包装壳）</summary>
    public T GetJson<T>() where T : class
    {
        if (_loadedJsonData.TryGetValue(typeof(T), out object? data) && data is T typedData)
        {
            return typedData;
        }

        throw new KeyNotFoundException($"JSON 资源类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    public bool TryGetJson<T>([NotNullWhen(true)] out T? json) where T : class
    {
        if (_loadedJsonData.TryGetValue(typeof(T), out object? data) && data is T typedData)
        {
            json = typedData;
            return true;
        }

        json = null;
        return false;
    }

    internal void PushTable<T>(Dictionary<int, T> dataMap) where T : TableBase
    {
        if (!_loadedTables.TryAdd(typeof(T), dataMap.ToFrozenDictionary()))
        {
            throw new InvalidOperationException($"表类型 {typeof(T).FullName} 已经注册");
        }
    }

    internal void PushJson<T>(T data) where T : class
    {
        if (!_loadedJsonData.TryAdd(typeof(T), data))
        {
            throw new InvalidOperationException($"JSON 资源类型 {typeof(T).FullName} 已经注册");
        }
    }

    /// <summary>在全量表与 JSON 加载完成后广播 OnFinalize，用于构建跨表外键与双向关联</summary>
    public void BroadcastOnFinalize()
    {
        foreach (System.Collections.IDictionary table in _loadedTables.Values)
        {
            foreach (TableBase row in table.Values)
            {
                row.OnFinalize();
            }
        }
    }

    /// <summary>在 OnFinalize 全部完成后广播 Verification，用于跨表完整性校验</summary>
    public void BroadcastVerification()
    {
        foreach (System.Collections.IDictionary table in _loadedTables.Values)
        {
            foreach (TableBase row in table.Values)
            {
                row.Verification();
            }
        }
    }
}
