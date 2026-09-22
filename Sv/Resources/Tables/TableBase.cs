using CsvHelper.Configuration.Attributes;

namespace Sv.Resources.Tables;

public abstract class TableBase
{
    public abstract int GetId();

    /// <summary>阶段 1：基础反序列化完成后触发，用于字段解析、枚举映射与字段预计算</summary>
    public virtual void OnLoad() { }

    /// <summary>阶段 2：全部表 OnLoad 完成后触发，用于构建跨表外键引用、建立双向关联与二级索引；跨表查询直接访问 GameTableCatalog.Instance</summary>
    public virtual void OnFinalize() { }

    /// <summary>阶段 3：全部表关联建立完成后触发，用于跨表约束、业务边界与资源完整性校验；跨表查询直接访问 GameTableCatalog.Instance</summary>
    public virtual void Verification() { }
}

public abstract class TableToolsTableBase : TableBase
{
    /// <summary>客户端 TableTools 的行序（客户端读取顺序），派生索引字段，不参与 TSV 读写。</summary>
    [Ignore]
    public int DataItemIndex { get; private set; }

    public void SetDataItemIndex(int index) => DataItemIndex = index;

    public int GetDataItemIndex() => DataItemIndex;

    // 未重写 GetId 的 TableTools表兜底
    public override int GetId() => DataItemIndex;
}
