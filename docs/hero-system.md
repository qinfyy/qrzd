# 神器使管理系统逆向记录

研究日期: 2026-09-26。范围为当前客户端反编译脚本与本地 Sv 工程的静态对照。

本文区分客户端已确认行为、当前 Sv 实现和后续实现建议。客户端代码不能证明官方服务端采用了什么数据库或内部类结构。初次研究没有修改业务代码或玩家存档；后续按永久角色规则修正了 GM 主线重置，见第 8 节。剧情临时角色与永久持有的边界见第 7 节。

## 1. 核心结论

- 神器使本体按资源 `heroId` 唯一管理，不是背包内按 UUID 生成的可重复角色实例。
- 常规拥有角色的等级直接取账号等级。星级、神器、觉醒、解放是不同的成长维度，不能合并成一个角色等级。
- 神器使本体、神器养成、影装实例、皮肤实例是不同对象。影装与皮肤使用 UUID，不意味着神器使也应使用 UUID。
- 客户端同时维护可用与禁用神器使。剧情禁用不会删除所有权或重建养成数据；剧情锁定又是独立名单。
- 剧情与试玩角色不是独立实体。它们的来源是关卡配置，随战斗数据下发，从不进入玩家存档。
- 当前 Sv 只持久化五个神器使本体字段，尚不是完整养成系统。登录能显示角色，不代表神器、装备、觉醒等已经实现。

入口: [ClientAvatar.py:214](D:/f7/Reverse/Script/py/entity/ClientAvatar.py:214)、[HeroMgrData.py:1067](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1067)、[HeroMgrData.py:484](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:484)。

## 2. 管理器与分类

登录时 `ClientAvatar` 创建 `HeroMgrData`，读取角色快照的 `heromgr`。它不是普通背包 `inv` 的一个分类。

| 字段 | 客户端含义 | 结构或注意事项 |
| --- | --- | --- |
| `hrs` | 可用神器使 | `[[heroId, heroData], ...]`，不是对象字典 |
| `bhrs` | 禁用神器使 | 同样是 ID 与完整数据的配对列表 |
| `c` | 剧情锁定名单 | ID 列表，与禁用分组独立 |
| `fl` | 好感锁定名单 | 读入后转为集合 |
| `cfs` | 待处理神器槽位状态 | `initFromDict` 直接索引，不能遗漏 |
| `ps` / `psc` | 暂存皮肤 / 获得时间 | 尚未归入拥有角色皮肤列表的数据 |
| `pp` / `ppc` | 暂存挂饰 / 获得时间 | 与角色拥有的挂饰区分 |
| `tp` / `htp` / `tpi` | 影装方案 / 角色关联 / 方案顺序 | 管理器级别数据 |
| `hrr` / `hsr` / `htr` | 回退 / 替换 / 转移记录 | 不应仅改当前星级而丢失记录 |

`banHeroes` 把同一个对象从 `heroes` 移到 `bannedHeroes`，`recoverBanHeroes` 再移回来。`getHero` 默认同时查询两组，所以“拥有”与“允许出战”必须分开判断。

资源 `hero_type` 为: 1 战士、2 坦克、3 法师、4 影袭、5 射手、6 辅助。相邻的 `BuffBelongHeroType` 枚举顺序不同，不可混用。`yijieti` 是资源中的异界体标志，会改变升星上限、材料和神器规则，不是装备类型。

依据: [HeroMgrData.py:1077](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1077)、[HeroMgrData.py:1123](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1123)、[const.py:1129](D:/f7/Reverse/Script/py/com/const.py:1129)。

## 3. 单个神器使需要保存什么

以下是客户端线上的短字段，不要求服务端存档照搬命名。

| 字段 | 含义 | 读入行为 |
| --- | --- | --- |
| `sl` / `so` | 星级档位 / 档位内小阶 | 必需 |
| `cf` / `ef` | 当前疲劳 / 好感值 | 必需 |
| `ar` | 神器养成数据 | 必需，内部结构见下文 |
| `ti` | 已装备影装库存 | 必需，包含 `mc` 与 `items` |
| `cat` | 待确认的神器属性阈值结果 | 必需，无结果可为空 |
| `oa` | 是否开启神器 | 默认 `False` |
| `fd` / `cfd` | 已攻略 / 本周攻略状态 | 默认 `False`，不是“黑化”标记 |
| `sr` | 战力缓存 | 默认 0，可由事实状态重新计算 |
| `sk` / `cs` | 皮肤 UUID 字典 / 当前皮肤 UUID | 角色本体与皮肤实例不能混淆 |
| `pa` / `wpa` / `pat` | 持有挂饰 / 穿戴挂饰 / 获得时间 | 列表 |
| `ls` / `lh` / `la` / `ld` | 解放阶段 / 生命、攻击、防御累计经验 | 各分支等级由经验和阶段上限推导 |
| `as` / `ass` | 觉醒阶段 / 觉醒外观开关 | 不等于 `sl` / `so` |
| `jbt` | 两个羁绊影装引用 | 默认两个空值 |
| `at` | 神器使获得时间 | 默认 0 |
| `pt` / `spt` / `htmr` / `htcmb` | 试炼通过与演习记录 | 独立于星级养成 |

神器 `ar` 的主要字段:

| 字段 | 含义 |
| --- | --- |
| `o` | 神器等级 |
| `ss` / `sl` | 槽位数 / 槽位数据列表，槽位含 `i`、`a` |
| `oa` | 已分配属性点 |
| `ot` / `rotc` | 属性阈值 / 阈值随机次数 |
| `fn` | 购买的属性点数量 |
| `aao` | 觉醒增加的神器等级上限部分 |
| `il` | 神器增幅等级 |

注意: 神器使根节点的 `oa` 是布尔开关，`ar.oa` 是属性点字典，不能共用含义。

`HeroData.getLevel()` 直接返回 `gg.avatar.level`；`updateHeroLevel` RPC 当前客户端实现只打印，不修改等级。普通角色最大 `sl=4, so=4`，异界体最大 `sl=5, so=4`。觉醒 UI 可以展示新的星级样式，但持久化仍是单独的 `as`。

依据: [HeroMgrData.py:273](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:273)、[HeroMgrData.py:915](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:915)、[HeroMgrMember.py:953](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:953)、[const.py:1156](D:/f7/Reverse/Script/py/com/const.py:1156)。`fd` 的含义由 [gonglueHeroes](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1095) 确认。

## 4. 下行同步不是任意字段更新

| RPC | 用途 | 必须注意 |
| --- | --- | --- |
| `addHero {h,d}` | 新获得神器使 | 已拥有时直接返回，不能用于更新既有角色 |
| `syncHeroMgrData {d}` | 重建整个管理器 | `d` 是含 `hrs`、`bhrs`、`cfs` 等字段的完整管理器 |
| `syncHeroesData {d}` | 更新已有角色集合 | `d` 为 ID/数据配对列表，每名角色必须携带完整初始化字段 |
| `syncHeroListData {sd}` | 替换已有角色对象 | 保持角色原来的可用/禁用分组 |
| `updateHero {h,d}` | 更新可用组中的单个角色 | 调用 `initFromDict`，不是稀疏 patch |
| `updateHeroData {h,d}` | 更新两组中可查询到的角色 | 同样调用 `initFromDict` |
| `syncHeroMgrPendingData {d}` | 同步暂存皮肤、挂饰 | 与角色列表同步分开 |
| `banHeroes {bhs}` / `recoverBanHeroes {rhs}` | 禁用 / 恢复 | 移动现有对象，不新增所有权 |

两个重要风险:

1. `syncHeroesData` 只发 `sl/so` 会缺字段报错；遗漏 `sk` 等可选字段又会清空相应客户端状态，不能把它当作通用字段补丁。
2. 嵌套的 `InvData.initFromDict` 调用的是 `updateItems`，不会删除这次列表中没有的旧影装。因此给已有角色发 `ti.items=[]` 不能保证卸下影装；需要显式卸装 RPC，或确实重建角色对象的同步路径。

依据: [HeroMgrMember.py:913](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:913)、[HeroMgrMember.py:2130](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:2130)、[HeroMgrMember.py:2292](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:2292)、[InvData.py:42](D:/f7/Reverse/Script/py/entity/avatar_attrs/InvData.py:42)。

## 5. 养成与穿戴协议

| 操作 | 客户端请求 | 下行处理 |
| --- | --- | --- |
| 升星 / 开启神器 | `incHeroStarOrder {h,cost,fkt}` | 同名回复 `{h,artifact}`；失败 `h=-1`，成功普通升星时客户端自行加一阶，开启神器时设开关 |
| 神器升级 | `incArtifactOrder {h,cost,fkt}` | 同名回复 `{h,r,c}`；客户端自行加一级并增加 COST |
| 神器一键升级 | `incArtifactOrderOneKey {h}` | 同名回复 `{h,r,c}`；使用返回的目标等级 |
| 神器增幅 | `incArtifactIncLv {h}` | 同名回复 `{h,i,c}`；设置增幅等级并增加 COST |
| 神器加点 | `updateArtifactAttrs {h,a}` | 同名回复 `{h,a,o}`；新属性点和旧属性点 |
| 解放突破 | `tupoLiberateStage {h}` | `tupoLiberateStageReply {h,s,c}` |
| 解放喂材料 | `upgradeLiberate {h,t,i}` | `upgradeLiberateReply {h,r}`，含各分支经验或错误 |
| 觉醒 | `upgradeAwakeStage {hid}` | `replyUpgradeAwakeStage {hid,as,aao}` |
| 批量装备影装 | `equipTreasureBatch {h,i,pi}` | `equipTreasureBatchReply {h,u,e,pi}`，先删除卸装引用，再写入装备数据 |
| 切换皮肤 | `setSkinRequest {h,sid}` | `setSkinReply {s,sid,ass}` |
| 穿戴挂饰 | `dressPendant {h,d,p}` | `replyDressPendant {r,h,d,p}` |

`cost` 是要消耗的影装 UUID 字符串列表，`fkt` 是替代材料 ID，不是金币数量。服务器应根据资源表校验碎片、普通材料、指定影装 ID/品质/星数及替代材料，而不能相信客户端已完成校验。

普通神器操作要求账号等级严格大于 `ARTIFACT_OPEN_LEVEL=30`，星级达到 `4/4` 且 `oa=True`。满星但尚未开启神器时，客户端仍走 `incHeroStarOrder`；将 `4/4` 一概视为不能操作，会阻断神器开启。异界体有单独分支，不能套用这套条件。

觉醒还检查资源 `open_awake`、开放时间、开启神器、阶段上限和材料。不能仅通过发送一个 `as` 值替代整个养成过程。

`incHeroStarOrder` 和普通 `incArtifactOrder` 的成功回复会让客户端自增。若先发送升级后的完整快照，再发送同一次成功回复，会产生再次自增的风险。需要明确包顺序，并正确完成失败回调与 UI 解锁。

依据: [HeroMgrMember.py:489](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:489)、[HeroMgrMember.py:593](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:593)、[HeroMgrMember.py:767](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:767)、[HeroMgrMember.py:1910](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:1910)、[HeroMgrMember.py:3013](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:3013)。

## 6. 影装、皮肤与属性

每个神器使创建 `InvData(invId=heroId)`，背包库存使用 `invId=0`。装备请求的 `i` 是 `UUID字符串 -> 来源invId` 的字典；回复 `e` 的条目是 `[UUID字符串, 来源heroId, 完整itemInfo]`。

客户端 `findTreasureByUUID` 收集引用同一 UUID 的全部神器使，再查询玩家背包。卸下全部角色的接口也会遍历这些引用。因此服务端数据模型需要表达多角色引用，不能把穿戴建模成“从背包移动到唯一角色的独占物品”。这不代表任何战斗模式都允许无限共用，出战组合限制仍需按玩法继续核对。

装备选择检查 `uniqueEquip`、`validHeroes`、是否允许操作和总 COST。神器升级、解放突破、攻略奖励等都可以增加该角色的 COST，不能永久固定为 25。销毁或消费影装时，也必须同步处理全部引用和相关羁绊槽位。

皮肤是 `SkinData` 实例，拥有资源 ID、UUID、染色/待确认染色、获得时间等；角色保存皮肤集合及当前 UUID。尚未拥有角色时取得的外观还有管理器级暂存路径，不能只往普通背包塞一个 ID。

面板属性由基础资源、账号等级、星级、神器、影装/套装、羁绊影装、解放和房间加成共同计算。技能主要从 `(heroId,sl,so)` 的 `hero_star_skill.cumSkills`、`(heroId,神器等级)` 的 `hero_artifact_attr.cumskills` 与觉醒配置推导；觉醒技能会覆盖对应普通技能。

依据: [InvMember.py:30](D:/f7/Reverse/Script/py/entity/avatar_members/InvMember.py:30)、[HeroMgrMember.py:972](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:972)、[HeroMgrMember.py:1024](D:/f7/Reverse/Script/py/entity/avatar_members/HeroMgrMember.py:1024)、[SkinData.py:65](D:/f7/Reverse/Script/py/com/utils/SkinData.py:65)、[HeroMgrData.py:611](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:611)、[HeroMgrData.py:800](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:800)。

## 7. 剧情临时角色与永久持有的边界

客户端不存在“试用角色”这一独立实体。剧情里能用的非玩家持有角色，是关卡配置直接指定的英雄 ID 列表，战斗时随战斗数据一起下发，根本不进入 `heroMgrData`。确认依据如下。

### 7.1 上阵名单来自关卡配置

`mission` 配置表的 `defaultHeroes` 和 `target_hero` 两个字段决定了哪些英雄能上阵，不要求玩家持有:

```python
defaultHeroes = MissionDataMod.data[sceneId].get('defaultHeroes')
if defaultHeroes:
    heroes = defaultHeroes[:]          # 直接用配置名单，覆盖玩家选择
```

主线与区域剧情关卡走 `system_type in MissionMode.CINE_SET`（即 `AREA_MISSION=1` 与 `MAIN_MISSION=2`），该分支会放宽持有校验:

```python
if missionMode in const.MissionMode.CINE_SET:
    if sceneId in const.DAERWEILA_SCENE_SET_2:      # 357-363
        target_hero = data.get('target_hero', ())
        heroes = [i for i in heroes if self.heroMgrData.getHero(i, withBan=False) or i in target_hero]
    elif sceneId in const.COC_HERO_LIMIT_SCENE:     # 482-488
        target_hero = data.get('target_hero', ())
        heroes = list(target_hero)                 # 完全替换为配置名单
    else:
        heroes = [i for i in heroes if self.heroMgrData.getHero(i, withBan=False)]
```

剧情事件战斗（`BattleType.EVENT`）在 `QIANSHI_SCENE_SET`（401006、401009-401012）有同样分支。`COC_HERO_LIMIT_SCENE` 更极端: 队伍被整个替换为 `target_hero`，玩家选人完全无效。

依据: [BattleStageMember.py:99](D:/f7/Reverse/Script/py/entity/avatar_members/BattleStageMember.py:99)、[BattleStageMember.py:152](D:/f7/Reverse/Script/py/entity/avatar_members/BattleStageMember.py:152)、[const.py:2084](D:/f7/Reverse/Script/py/com/const.py:2084)、[const.py:1961](D:/f7/Reverse/Script/py/com/const.py:1961)。

### 7.2 未持有时客户端读取的是默认值

战斗结算要读取每个上阵英雄的皮肤、神器、觉醒、挂饰。这些 getter 对未持有的英雄全部返回兜底值，不报错也不建对象:

| 方法 | 未持有时返回 |
| --- | --- |
| `getHeroSkinData` | `SkinData.getFakePersistentDict(heroId)`，伪造 UUID 的临时皮肤 |
| `getArtifactInfo` | `(False, 0)`，视为未开启神器 |
| `getAwakeStage` | `0` |
| `getPendantInfo` | `[]` |

`getFakePersistentDict` 只填 `{i, u, cd, pd, dn}`，其中 `u` 是当场 `IdManager.genid()` 生成的临时 UUID。这正说明皮肤、影装、觉醒都属于“持有”这一事实，剧情角色不进入玩家的养成对象。

依据: [HeroMgrData.py:1286](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1286)、[HeroMgrData.py:1330](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1330)、[HeroMgrData.py:1339](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1339)、[HeroMgrData.py:1349](D:/f7/Reverse/Script/py/entity/avatar_attrs/HeroMgrData.py:1349)、[SkinData.py:91](D:/f7/Reverse/Script/py/com/utils/SkinData.py:91)。

### 7.3 剧情角色的属性来自关卡表

剧情角色的战斗数值由 `mission_hero_attr` 配置提供，按 `(sceneId, heroId)` 二级键索引，字段包含 `skin_id` 与 `awake`。这张表同时被战斗选择界面和战斗结算读取，说明它是关卡侧数据，与玩家存档无关。

`PVE_FANTASY`（脑洞）战斗走更彻底的隔离: 神器、觉醒、挂饰全部强制置为 `False/0/[]`，皮肤一律用伪造数据。

依据: [mission_hero_attr.py](D:/f7/Reverse/Script/py/com/data/cdata/mission_hero_attr.py)、[CombatMember.py:327](D:/f7/Reverse/Script/py/entity/avatar_members/CombatMember.py:327)、[CombatMember.py:345](D:/f7/Reverse/Script/py/entity/avatar_members/CombatMember.py:345)、[city_choose_hero.py:139](D:/f7/Reverse/Script/py/game_scene/city_choose_hero.py:139)。

### 7.4 抽卡试玩是另一条路径

`HeroBuyPreviewMember` 的 `requestTryPlayBattle` 是抽卡预览试玩，走 `combatOfflineRequest(BattleType.HUODONG, scene_id, [hero_id], ...)`。它同样只是把 `hero_id` 放进本次战斗的 `heroes` 列表，不写 `heroMgrData`，战斗结束只记录 `hero_rewards_done[hero_id] = danchi_id`。这条路径与剧情试用共用同一个结论: 试玩不产生持有关系。

依据: [HeroBuyPreviewMember.py:25](D:/f7/Reverse/Script/py/entity/avatar_members/ActivityMembers/HeroBuyPreviewMember.py:25)、[HeroBuyPreviewMember.py:35](D:/f7/Reverse/Script/py/entity/avatar_members/ActivityMembers/HeroBuyPreviewMember.py:35)。

### 7.5 对服务端的约束

1. **不要为剧情角色建存档。** 剧情与试玩英雄的来源是关卡配置和服务端下发的战斗数据，不进 `HeroState`，不占神器使列表，重登也不会恢复。
2. **重置可以放心清剧情状态。** 之前把 `ResetStoryHeroes` 改为 `ResetStoryState`、让 `/clear` 与 `/resetweek` 保留全部已获得神器使的决定是正确的，不需要为“可能被误删的试用角色”额外补救——因为试用角色从未被存档。
3. **战斗校验要区分来源。** 服务端校验上阵名单时，主线与区域关卡必须放行配置表里的 `defaultHeroes` / `target_hero`，不能只查玩家是否持有；否则剧情关卡直接无法开始。
4. **剧情角色的养成数据一律取关卡表。** 战斗构建时按 `(sceneId, heroId)` 查 `mission_hero_attr`，查不到再回退到玩家养成数据，与客户端的兜底行为保持一致。

## 8. 当前 Sv 的实际实现

持久化路径是 `Player.SaveData.HeroMgrComp -> PlayerSaveData Protobuf -> Players.Data SQLite BLOB`。不需要另建神器使关系表，也不应照搬 bh2 装备实例的角色模型。

| 部分 | 当前状态 |
| --- | --- |
| 所有权 | `HeroMgrComp.heroes` 保存 `HeroState`，按 `heroId` 查找和去重 |
| 已存字段 | 本体五项 + 神器开关/等级/增幅/加点、觉醒、解放、影装引用、皮肤、挂饰、羁绊、攻略状态、获得时间 |
| 剧情状态 | 管理器保存禁用原因和剧情锁定名单；已有禁用/恢复 RPC |
| 解锁 | `Unlock` 校验资源 ID，当前统一创建 `1/1`；尚未按获取来源完整区分初始养成 |
| 登录快照 | `ar` 固定初始等级/属性，`ti` 固定 COST 25 和空装备，`cat=null` |
| 升星 | 请求只允许空 `cost` 且 `fkt=0`；调用只改星级，没有扣除碎片等材料；硬编码止于 `4/4` |
| 神器开启 | 回复始终 `artifact=false`，未保存 `oa`，未实现满星后的开启路径 |
| 其他养成 | 尚未保存神器等级/加点、装备引用、皮肤、挂饰、觉醒、解放和攻略状态 |
| 组队与疲劳 | 已校验人数、重复角色、所有权、禁用、剧情锁与疲劳；战斗/都市行动会消费疲劳 |
| 战斗属性 | 当前主要构建基础与星级数据；影装相关数据为空，神器开启与觉醒写成关闭/0 |
| GM | `/hero`、`/allhero` 解决解锁，不等于完整养成 |

依据: [ServerProto.proto:106](D:/f7/Sv/Database/ServerProto.proto:106)、[HeroMgrLogic.cs:62](D:/f7/Sv/Game/HeroMgrLogic.cs:62)、[PlayerPackets.cs:133](D:/f7/Sv/Gateway/Packets/PlayerPackets.cs:133)、[PlayerHandlers.cs:26](D:/f7/Sv/Gateway/Handlers/PlayerHandlers.cs:26)、[CombatLogic.cs:90](D:/f7/Sv/Game/CombatLogic.cs:90)、[GameDatabase.cs:116](D:/f7/Sv/Database/GameDatabase.cs:116)。

2026-09-26 重置修正: 玩家获得的神器使属于永久资产，不能随周目或主线重置删除。原实现的 `ResetStoryHeroes` 曾清空角色集合，现已改为 `ResetStoryState`。GM `/clear` 与 `/resetweek` 保留所有已获得神器使和全部养成字段，仅重置周期状态: 疲劳恢复到资源表上限、好感归零、本周攻略清空，并清除剧情禁用与锁定。养成数据一律保留在原角色对象上，不通过重建默认角色覆盖。

GM 获取方法: 游戏内发送 `/hero <id>` 解锁指定神器使，`/allhero` 补齐全部。HTTP 或控制台使用 `/hero <id> @<玩家UID>`、`/allhero @<玩家UID>` 指定玩家；已有角色不会重新创建或覆盖养成。

依据: [CityCommands.cs:68](D:/f7/Sv/GameMaster/CityCommands.cs:68)、[WeekNumLogic.cs:174](D:/f7/Sv/Game/WeekNumLogic.cs:174)、[HeroMgrLogic.cs:54](D:/f7/Sv/Game/HeroMgrLogic.cs:54)。

## 9. 服务端实现建议

以下是后续落地顺序。2026-09-26 已完成第 1 步与第 2 步的基础成长闭环，其余仍待实现。

1. 扩充现有 Protobuf 存档中的 `HeroState` 与管理器字段，兼容旧存档。保存实际养成、外观和穿戴引用；职业、基础属性、技能配置继续从资源表取得。
2. 在 `HeroMgrLogic` 内集中处理拥有、禁用、开启神器、升星、神器升级、解放与觉醒规则。先补齐基础成长闭环，再接外观、洗炼和回退等功能。
3. 在玩家锁内先完成全部校验，再通过 `InventoryLogic`、`PlayerProfileLogic` 等资产所属模块扣材料；不要跨模块直接改其他 Comp。变更标脏后统一 `player.Save()`，按客户端语义发同步与结果包。
4. 区分新获取、已有角色更新、剧情禁用与恢复。重复获取的奖励转换由获取来源和资源表决定，不生成第二个同 heroId 的神器使。客户端已有记忆结晶和通用碎片兑换计算，但不能把一种卡池规则当作全部来源的规则。
5. 建立影装 UUID 的统一资产状态和多角色穿戴引用，保证消耗、升级、卸装、方案与羁绊同步一致。不要复制多份可以独立升级的同 UUID 资产。
6. 从同一组资源与存档状态生成登录快照、养成回复、战力与战斗数据，避免“面板成长了，进战斗还是旧数值”。
7. 将账号长期养成与本周剧情、疲劳、好感等周期状态分开制定重置规则。当前 GM 重置已经保留神器使和既有养成；未来正常跨周仍应保留永久角色，其他周期字段的规则需继续沿剧情/周目协议确认。

重复获取相关参考: [LuckyDrawData.py:1219](D:/f7/Reverse/Script/py/entity/avatar_attrs/LuckyDrawData.py:1219)。第 7 节的边界结论已并入第 1 条: 剧情与试玩角色不进存档，重置只需处理剧情状态。

## 10. 2026-09-26 已实现

存档扩充（`ServerProto.proto`）: `HeroState` 新增神器开关、等级、增幅、属性点、觉醒、解放、影装 COST、装备引用、皮肤、挂饰、羁绊与攻略状态；新增 `TreasureInstance` 保存影装实例，使同一 UUID 可被多角色引用而不复制资产；`HeroMgrComp` 新增影装池、好感锁与外观暂存。proto3 新增字段向后兼容，旧存档可直接读取，缺失字段取默认值。

新增资源表: `HeroStarData`（升星碎片消耗）、`ArtifactLevelData`（神器升级消耗）、`ArtifactAttrLevelData`（每级属性点）、`AwakeConsumeData`（觉醒材料与神器等级门槛）、`LiberateLvData`（解放属性）、`HeroFragmentItemData`（heroId 到碎片物品 ID 的索引）。启动日志确认全部加载成功。

`HeroMgrLogic` 改动:

1. `ToSnapshot` 改为输出真实养成状态，字段集合与客户端 `initFromDict` 对齐，不再固定 `ar.o=1`、`ti.mc=25` 与空装备。
2. `IncStarOrder` 补齐碎片扣除、异界体 5/4 上限分支，以及满星后开启神器的路径；通过 `out` 返回 `artifact` 标志供回复包使用。
3. 新增 `IncArtifactLevel` 与 `UpgradeAwakeStage`，按资源表校验等级门槛并经 `InventoryLogic` 扣材料。
4. 移除了 `cost` 必须为空且 `fkt=0` 的解析限制，改为解析后由服务端按资源表校验。
5. `ResetStoryState` 明确只重置周期状态，养成字段全部保留。

两处需要留意的实现事实:

- `hero.json` 不含 `fragment` 与 `yijieti` 字段，碎片 ID 与异界体判定改由 `fragment_item` 表反查（`heroid` 严格一对一，138 行）。
- 客户端在 `artifact=false` 时会自行调用 `incStarOrder()`，因此服务端只推进存档并在回复里带 `artifact` 标志，不再下发已自增的完整快照。

## 验证边界

已完成静态源码对照与关键调用链追踪。本轮没有操作客户端、执行养成 RPC 或改动存档；多人同场装备共用限制、各获取来源的重复奖励、完整跨周保留规则仍需后续专项确认。未编写或运行单元测试。
