# 客户端物品系统与 GM 发放

## 逆向依据

- `Reverse/Script/py/com/const.py` 的 `ItemType`、`VirtualItem`、`VirtualItemSub`、`CombatAttrs`。
- `Reverse/Script/py/entity/avatar_attrs/ItemData.py` 的 `initFromDict`、`loadTreasure`。
- `Reverse/Script/py/entity/avatar_attrs/InvData.py` 的分类容量、堆叠判断和 UUID 索引。
- `Reverse/Script/py/entity/avatar_members/InvMember.py` 的 `updateItems`、`deleteItems`。
- `Reverse/Script/py/cocos/panels/bag_main.py` 与 `com/data/cdata/bag_main_show.py` 的背包分页。
- `Reverse/Script/py/com/utils/helpers.py` 的 `_check_item_have` 验证虚拟物品的实际数据归属。
- `StaticResParser/output/dbx/item_data.json`、`hero_treasure.json` 是当前服务端使用的客户端资源表。

bh2 只参考 `give` 单 ID 发放、`giveall` 分类补齐/批量累加和显式数量的交互形式，不沿用它的武器、服装、徽章等分类。

## 逻辑分类

客户端 `type` 不是背包页签，当前常量如下：

| type | 客户端常量 | 含义 |
| --- | --- | --- |
| 1 | COMMON | 普通物品，包含多种材料和活动道具 |
| 2 | TASK | 任务物品 |
| 3 | HERO_FRAGMENT | 神器使灵魂碎片 |
| 4 | HERO_CRYSTAL | 魂晶 |
| 5 | HERO_TREASURE | 影装 |
| 6 | REFRESH | 洗炼材料 |
| 7 | BREAK | 影装突破材料 |
| 8 | VIRTUAL | 虚拟资产，由 virtual_sub_type 决定归属 |
| 9 | HERO_ITEM | 神器使材料 |
| 10 | GIFT | 礼物 |
| 11 | DYE | 染色材料 |
| 13 | XINWU | 信物 |
| 14 | XUNZHANG | 勋章物品常量；当前徽章资源主要使用虚拟子类型 24 |
| 15 | TREASUREEXPITEM | 影装经验/专属影装材料 |
| 16 | SKIN_TEXTURE | 纹染材料，不是已解锁皮肤 |
| 17 | MEMORY_CRYSTAL | 记忆结晶 |
| 18 | HERO_HOME_GIFT | 房间礼物 |
| 19 | SPE_LIBERATE_ITEM | 限定解放材料 |
| 20 | FURNITURE | 家具 |
| 21 | CHOOSE_HERO_FRAGMENT | 自选神器使碎片物品 |
| 22 | YIJIETI_HERO_FRAGMENT | 异界体碎片 |
| 23 | YIJIETI_HERO_MEMORY_CRYSTAL | 异界体结晶 |
| 24 | XKJL_PET_CHANGE | 宠物兑换物 |
| 25/26 | ARITIFACT_INC_FRAGMENT / ARITIFACT_INC_CRYSTAL | 常量存在，但未纳入当前 InvData 分类，当前资源没有对应条目 |

`InvData` 实际按六个组维护背包容量：

| GM 分类 | 客户端组 | type |
| --- | --- | --- |
| common | CATEGORY_COMMON | 1,3,4,6,7,8,9,11,14,15,16,17,18,19,22,23；GM 排除虚拟类型 8 |
| task | CATEGORY_TASK | 2 |
| treasure | CATEGORY_TREASURE | 5 |
| gift | CATEGORY_GIFT | 10,21,24 |
| furniture | CATEGORY_FURNITURE | 20 |
| xinwu | CATEGORY_XINWU | 13 |
| fragment | CATEGORY_FRAGMENT 筛选 | 3,22 |

资源中另有 `type=51` 的花火大会信物 12098，但当前客户端常量和 `InvData.type2Category` 没有对应分类，暂不发放。

## 显示分类

背包 `bag_main` 按 `ui_class` 筛选，不按 `type` 筛选：

| GM 筛选 | ui_class | 分页 |
| --- | --- | --- |
| ui1 | 1 | 钱包 / 贵重物品 |
| ui2 | 2 | 钱包 / 兑换货币 |
| ui3 | 3 | 道具 / 活动道具 |
| ui4 | 4 | 道具 / 强化材料 |
| ui5 | 5 | 道具 / 影装材料 |
| ui6 | 6 | 道具 / 房间道具 |
| ui7 | 7 | 道具 / 染色材料 |
| ui12 | 12 | 道具 / 其它 |

衣橱从神器使皮肤数据读取，徽章页从 `badgeData.current_badges` 读取。影装、信物和家具各有专用界面。不能因为物品存在于 item_data 就直接插入背包。

## 数据与同步

普通物品通过 `inv.items` 持久化，在客户端建立 UUID、物品 ID、类型三个索引。`updateItems(i)` 使用 `u` 表示 BSON ObjectId，`i` 表示物品 ID，`w` 表示该堆叠数量；`ctt` 为创建时间。增量中同 UUID 表示更新，不是额外增加同等数量。

`wrap>0` 时先填现有堆叠，再创建新堆叠；`wrap=0` 表示不堆叠，并非不可发放。影装无论表中 wrap 如何，每件都需要独立 UUID、`w=1` 与完整 `tr`。

影装 `tr.lv` 为等级，`tr.cl` 为品质，`tr.ft` 为固定特技，`tr.attr` 为属性列表。属性 `n` 必须使用 `f<CombatAttrs 索引>` 或 `t<索引>`，例如 `_phy_str_delta` 写作 `f30`、生命成长写作 `f34`，不能原样发送 `_hp_delta`，否则客户端执行 `int(attrName[1:])` 时异常。固定属性和第三属性分别从 `secondAttrs`、`thridAttrs` 的范围生成。

虚拟资产不是普通背包实体。当前发放支持三个已持久化的钱包字段：金币 102 -> money、晶尘 89 -> crystal、欧泊 90 -> summonCoin。晶钻 80 -> yuanbao，当前未实现。皮肤、挂饰、徽章、称号等也不能伪装成普通堆叠。

## 命令

```text
/give 1 x100
/give treasure 1001 x2 lv40 cl4
/give currency 90 x100
/giveall
/giveall treasure lv40 cl4
/giveall furniture
/giveall xinwu
/giveall ui4 x100
/giveall ui5 x100
/giveall currency x100
```

游戏内默认修改自己；HTTP 使用 `@uid`，例如 `/give 1 x100 @1`。`give` 数量默认 1，也接受旧的裸数字数量形式；旧 `item` 名称已移除。`g`、`ga` 分别为两个新命令的简写。

`giveall` 默认 `all`，仅补齐已发布且配置完整的影装、家具、信物，不包含普通材料、任务物品、礼物或货币。按物品 ID 跳过已有条目，不修改其数量和影装养成。需要同一 ID 多份时用 `give`。

普通分类和 UI 分页必须指定数量，每次都累加。UI 分页始终排除虚拟资产；`giveall currency` 仅累加已支持的三种余额。批量只遍历 `release=true`，影装之匣 155 因缺少完整影装配置而跳过。启动生成的 `Sv/Handbook.txt` 列出可发放 ID、中文名称、type、ui_class 与堆叠上限。

状态修改和存盘均在玩家锁内；同步先收集，存盘成功后按每包最多 50 个条目下发。异常会恢复本次操作前的状态，不先发成功包。普通物品每 ID 单次最多 100000 个且最多 100 组，影装/非堆叠物品最多 100 件，批量最多预估 10000 组；超出限制整批拒绝。

客户端普通/影装分类的常规容量为 500，任务/礼物/家具为 400，信物为 1000；影装还有 subbag 子容量。GM 发放不扩容、也不强制常规容量，所以大量材料可能触发客户端背包满判定并限制普通奖励领取，应按实际需要选择分页和数量。

这里只实现物品生成、堆叠、分类批量发放、持久化与同步。获得礼包、家具、信物等实体不代表对应使用、房间布置、合成或养成 RPC 已全部实现。
