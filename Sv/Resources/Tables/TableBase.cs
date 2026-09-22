using System.Text.Json;

namespace Sv.Resources.Tables;

public abstract class TableBase
{
    public abstract int GetId();

    /// <summary>由资源加载器在 OnLoad 前传入 DBX 原始键（适用于复合键或表数据中未显式包含主键字段的表）</summary>
    public virtual void SetKey(JsonElement key) { }

    /// <summary>阶段 1：基础反序列化完成后触发，用于字段解析、枚举映射与字段预计算</summary>
    public virtual void OnLoad() { }

    /// <summary>阶段 2：全部表 OnLoad 完成后触发，用于构建跨表外键引用、建立双向关联与二级索引；跨表查询直接访问 GameTableCatalog.Instance</summary>
    public virtual void OnFinalize() { }

    /// <summary>阶段 3：全部表关联建立完成后触发，用于跨表约束、业务边界与资源完整性校验；跨表查询直接访问 GameTableCatalog.Instance</summary>
    public virtual void Verification() { }
}
