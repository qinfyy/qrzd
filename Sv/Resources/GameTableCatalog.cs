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

    private interface ITableEntry
    {
        object RawMap { get; }
        void FinalizeRows();
        void VerifyRows();
    }

    private sealed class TableEntry<T>(FrozenDictionary<int, T> map) : ITableEntry where T : TableBase
    {
        public object RawMap => map;

        public void FinalizeRows()
        {
            foreach (T row in map.Values)
            {
                row.OnFinalize();
            }
        }

        public void VerifyRows()
        {
            foreach (T row in map.Values)
            {
                row.Verification();
            }
        }
    }

    // Type -> TableEntry (FrozenDictionary<int, T>)
    private readonly Dictionary<Type, ITableEntry> _loadedTables = [];

    // Type -> 反序列化对象
    private readonly Dictionary<Type, object> _loadedJsonData = [];

    public int LoadedTableCount => _loadedTables.Count;

    public int LoadedJsonCount => _loadedJsonData.Count;

    public T? GetDataById<T>(int id) where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out ITableEntry? entry) && entry.RawMap is FrozenDictionary<int, T> map)
        {
            return map.GetValueOrDefault(id);
        }

        throw new KeyNotFoundException($"资源表类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    public bool TryGetDataById<T>(int id, [NotNullWhen(true)] out T? data) where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out ITableEntry? entry) && entry.RawMap is FrozenDictionary<int, T> map)
        {
            return map.TryGetValue(id, out data);
        }

        data = null;
        return false;
    }

    /// <summary>获取全量行集合</summary>
    public IReadOnlyList<T> GetAllData<T>() where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out ITableEntry? entry) && entry.RawMap is FrozenDictionary<int, T> map)
        {
            return map.Values is IReadOnlyList<T> list ? list : [.. map.Values];
        }

        throw new KeyNotFoundException($"资源表类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    /// <summary>直接获取整张表的只读字典</summary>
    public FrozenDictionary<int, T> GetTable<T>() where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out ITableEntry? entry) && entry.RawMap is FrozenDictionary<int, T> map)
        {
            return map;
        }

        throw new KeyNotFoundException($"资源表类型 {typeof(T).FullName} 尚未注册或未加载");
    }

    public bool TryGetTable<T>([NotNullWhen(true)] out FrozenDictionary<int, T>? table) where T : class
    {
        if (_loadedTables.TryGetValue(typeof(T), out ITableEntry? entry) && entry.RawMap is FrozenDictionary<int, T> map)
        {
            table = map;
            return true;
        }

        table = null;
        return false;
    }

    /// <summary>获取 JSON 配置对象（直接返回强类型实体）</summary>
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
        if (!_loadedTables.TryAdd(typeof(T), new TableEntry<T>(dataMap.ToFrozenDictionary())))
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
        foreach (ITableEntry entry in _loadedTables.Values)
        {
            entry.FinalizeRows();
        }
    }

    /// <summary>在 OnFinalize 全部完成后广播 Verification，用于跨表完整性校验</summary>
    public void BroadcastVerification()
    {
        foreach (ITableEntry entry in _loadedTables.Values)
        {
            entry.VerifyRows();
        }
    }
}
