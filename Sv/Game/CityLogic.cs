using Sv.Database;
using Sv.Resources;
using Sv.Resources.Tables;

namespace Sv.Game;

public sealed class CityLogic(Player player) : PlayerLogicBase(player)
{
    private CityComp Comp => Player.SaveData.CityComp;

    public int ActionVal
    {
        get => Comp.ActionVal;
        set
        {
            if (Comp.ActionVal != value)
            {
                Comp.ActionVal = value;
                MarkDirty();
            }
        }
    }

    public int DevelopVal
    {
        get => Comp.DevelopVal;
        set
        {
            if (Comp.DevelopVal != value)
            {
                Comp.DevelopVal = value;
                MarkDirty();
            }
        }
    }

    public int DevelopValCount
    {
        get => Comp.DevelopValCount;
        set
        {
            if (Comp.DevelopValCount != value)
            {
                Comp.DevelopValCount = value;
                MarkDirty();
            }
        }
    }

    public int BuildFund
    {
        get => Comp.BuildFund;
        set
        {
            if (Comp.BuildFund != value)
            {
                Comp.BuildFund = value;
                MarkDirty();
            }
        }
    }

    public int ForceVal
    {
        get => Comp.FatigueVal;
        set
        {
            if (Comp.FatigueVal != value)
            {
                Comp.FatigueVal = value;
                MarkDirty();
            }
        }
    }

    public int FatigueVal
    {
        get => ForceVal;
        set => ForceVal = value;
    }

    public int EventVal
    {
        get => Comp.EventVal;
        set
        {
            if (Comp.EventVal != value)
            {
                Comp.EventVal = value;
                MarkDirty();
            }
        }
    }

    public int ResearchVal
    {
        get => Comp.ResearchVal;
        set
        {
            if (Comp.ResearchVal != value)
            {
                Comp.ResearchVal = value;
                MarkDirty();
            }
        }
    }

    public int PatrolNum
    {
        get => Comp.PatrolNum <= 0 ? 1 : Comp.PatrolNum;
        set
        {
            if (Comp.PatrolNum != value)
            {
                Comp.PatrolNum = value;
                MarkDirty();
            }
        }
    }

    protected internal override void OnCreate()
    {
        EnsureDefaultAreas();
    }

    protected internal override void OnLoad()
    {
        EnsureDefaultAreas();
        RecalculateStats();
    }

    public void EnsureDefaultAreas()
    {
        if (Comp.Areas.Count == 0)
        {
            // Area 1: 中央庭 (FREE)
            AreaState a1 = new()
            {
                Id = 1,
                Status = 0, // FREE
                Cr = false,
                Lock = false,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 0,
                ResearchVal = 5,
                ForceVal = 5,
                DevelopVal = 5,
            };
            a1.Buildings.Add(new BuildingState { Slot = 1, BuildingId = 1, Uuid = Guid.NewGuid().ToString("N") });
            Comp.Areas.Add(a1);

            // Area 2: 高校学园 (FIGHT，初始关卡 1101)
            Comp.Areas.Add(new AreaState
            {
                Id = 2,
                Status = 1, // FIGHT
                Cr = false,
                Lock = false,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 1101,
            });

            // Area 3: 东方古街 (FALL)
            Comp.Areas.Add(new AreaState
            {
                Id = 3,
                Status = 2, // FALL
                Cr = false,
                Lock = true,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 2101,
            });

            // Area 4: 中央城区 (FALL)
            Comp.Areas.Add(new AreaState
            {
                Id = 4,
                Status = 2, // FALL
                Cr = false,
                Lock = true,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 3101,
            });

            // Area 5: 研究所 (FALL)
            Comp.Areas.Add(new AreaState
            {
                Id = 5,
                Status = 2, // FALL
                Cr = false,
                Lock = true,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 4101,
            });

            // Area 6: 海湾侧城 (FALL)
            Comp.Areas.Add(new AreaState
            {
                Id = 6,
                Status = 2, // FALL
                Cr = false,
                Lock = true,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 5101,
            });

            // Area 7: 旧城区 (FALL)
            Comp.Areas.Add(new AreaState
            {
                Id = 7,
                Status = 2, // FALL
                Cr = false,
                Lock = true,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 6101,
            });

            // Area 8: 港湾区 (FALL)
            Comp.Areas.Add(new AreaState
            {
                Id = 8,
                Status = 2, // FALL
                Cr = false,
                Lock = true,
                Level = 1,
                MaxBuildingNum = 4,
                CurrentStage = 7101,
            });

            ActionVal = 24;
            PatrolNum = 1;
            ResearchVal = 5;
            ForceVal = 5;
            DevelopVal = 5;
            MarkDirty();
        }
        else
        {
            if (ActionVal <= 0) ActionVal = 24;
            if (PatrolNum <= 0) PatrolNum = 1;
        }
    }

    public void RecalculateStats()
    {
        int totalRv = 0;
        int totalFv = 0;
        int totalDv = 0;

        foreach (AreaState area in Comp.Areas)
        {
            int areaRv = 0;
            int areaFv = 0;
            int areaDv = 0;

            foreach (BuildingState b in area.Buildings)
            {
                if (GameTableCatalog.Instance.TryGetDataById<CityBuildingData>(b.BuildingId, out CityBuildingData? row))
                {
                    areaRv += row.ResearchVal;
                    areaFv += row.ForceVal;
                    areaDv += row.DevVal;
                }
            }

            area.ResearchVal = areaRv;
            area.ForceVal = areaFv;
            area.DevelopVal = areaDv;

            if (area.Status == 0) // FREE
            {
                totalRv += areaRv;
                totalFv += areaFv;
                totalDv += areaDv;
            }
        }

        ResearchVal = Math.Max(5, totalRv);
        ForceVal = Math.Max(5, totalFv);
        DevelopVal = Math.Max(5, totalDv);
    }

    public static Dictionary<string, object> GetAreaStageDict(int areaId, int currentStage, int status)
    {
        if (status == 0) // FREE
        {
            return new Dictionary<string, object>
            {
                ["cs"] = 0,
                ["sl"] = Array.Empty<object>()
            };
        }

        int baseStage = areaId switch
        {
            2 => 1101,
            3 => 2101,
            4 => 3101,
            5 => 4101,
            6 => 5101,
            7 => 6101,
            8 => 7101,
            _ => 1101,
        };

        int cs = currentStage > 0 ? currentStage : baseStage;
        object[] stageList = new object[6];
        for (int i = 0; i < 6; i++)
        {
            stageList[i] = new object[] { baseStage + i };
        }

        return new Dictionary<string, object>
        {
            ["cs"] = cs,
            ["sl"] = stageList
        };
    }

    public Dictionary<string, object>? ToAreaSnapshot(int areaId)
    {
        AreaState? area = Comp.Areas.FirstOrDefault(a => a.Id == areaId);
        if (area is null) return null;

        Dictionary<string, object> bdDict = new();
        foreach (BuildingState b in area.Buildings)
        {
            bdDict[b.Slot.ToString()] = new Dictionary<string, object>
            {
                ["id"] = b.BuildingId,
                ["uuid"] = b.Uuid,
            };
        }

        return new Dictionary<string, object>
        {
            ["id"] = area.Id,
            ["st"] = new object[] { area.Status, area.Cr, area.Lock },
            ["lv"] = Math.Max(1, area.Level),
            ["mbn"] = Math.Max(4, area.MaxBuildingNum),
            ["bd"] = bdDict,
            ["dbd"] = new Dictionary<string, object>(),
            ["rv"] = area.ResearchVal,
            ["fv"] = area.ForceVal,
            ["dv"] = area.DevelopVal,
            ["pl"] = false,
            ["stage"] = GetAreaStageDict(area.Id, area.CurrentStage, area.Status)
        };
    }

    public Dictionary<string, object> ToAreasSnapshot()
    {
        EnsureDefaultAreas();
        Dictionary<string, object> result = new();
        for (int i = 1; i <= 8; i++)
        {
            Dictionary<string, object>? areaSnap = ToAreaSnapshot(i);
            if (areaSnap is not null)
            {
                result[$"area_{i}"] = areaSnap;
            }
        }
        return result;
    }

    public Dictionary<string, object> ToCityDataSnapshot()
    {
        return new Dictionary<string, object>
        {
            ["dv"] = DevelopVal,
            ["dvc"] = DevelopValCount,
            ["av"] = ActionVal,
            ["pv"] = 240,
            ["lpc"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["bf"] = BuildFund,
            ["fv"] = ForceVal,
            ["ev"] = EventVal,
            ["rv"] = ResearchVal,
            ["pn"] = PatrolNum,
            ["acr"] = Array.Empty<object>(),
            ["uc"] = false,
            ["rbn"] = 0
        };
    }

    public bool Build(int areaId, int slot, int buildingId, int[] heroIds, out string? error)
    {
        error = null;
        if (ActionVal < 2)
        {
            error = "行动力不足";
            return false;
        }

        AreaState? area = Comp.Areas.FirstOrDefault(a => a.Id == areaId);
        if (area is null || area.Status != 0)
        {
            error = "区域未解放或不存在";
            return false;
        }

        if (slot < 1 || slot > Math.Max(4, area.MaxBuildingNum))
        {
            error = "建筑槽位无效";
            return false;
        }

        if (area.Buildings.Any(b => b.Slot == slot))
        {
            error = "槽位已有建筑";
            return false;
        }

        foreach (int heroId in heroIds)
        {
            if (!Player.HeroMgr.ConsumeFatigue(heroId, 5))
            {
                error = "神器使疲劳不足";
                return false;
            }
        }

        ActionVal -= 2;
        area.Buildings.Add(new BuildingState
        {
            Slot = slot,
            BuildingId = buildingId,
            Uuid = Guid.NewGuid().ToString("N"),
        });

        RecalculateStats();
        MarkDirty();
        return true;
    }

    public bool LevelUpArea(int areaId, int levelUpVal, int[] heroIds, out string? error)
    {
        error = null;
        if (ActionVal < 2)
        {
            error = "行动力不足";
            return false;
        }

        AreaState? area = Comp.Areas.FirstOrDefault(a => a.Id == areaId);
        if (area is null || area.Status != 0)
        {
            error = "区域未解放或不存在";
            return false;
        }

        if (area.Level + levelUpVal > 5)
        {
            error = "区域已达到最大等级";
            return false;
        }

        foreach (int heroId in heroIds)
        {
            if (!Player.HeroMgr.ConsumeFatigue(heroId, 5))
            {
                error = "神器使疲劳不足";
                return false;
            }
        }

        ActionVal -= 2;
        area.Level += levelUpVal;
        area.MaxBuildingNum = Math.Min(8, 4 + (area.Level - 1));

        MarkDirty();
        return true;
    }

    public bool Patrol(int areaId, int[] heroIds, out string? error)
    {
        error = null;
        if (ActionVal < 2)
        {
            error = "行动力不足";
            return false;
        }

        AreaState? area = Comp.Areas.FirstOrDefault(a => a.Id == areaId);
        if (area is null)
        {
            error = "区域不存在";
            return false;
        }

        foreach (int heroId in heroIds)
        {
            if (!Player.HeroMgr.ConsumeFatigue(heroId, 5))
            {
                error = "神器使疲劳不足";
                return false;
            }
        }

        ActionVal -= 2;
        PatrolNum += 1;

        foreach (int heroId in heroIds)
        {
            Player.HeroMgr.AddFriendly(heroId, 5);
        }

        MarkDirty();
        return true;
    }

    public bool DestroyBuilding(string uuid, out int affectedAreaId)
    {
        affectedAreaId = 0;
        foreach (AreaState area in Comp.Areas)
        {
            BuildingState? b = area.Buildings.FirstOrDefault(x => x.Uuid == uuid);
            if (b is not null)
            {
                area.Buildings.Remove(b);
                affectedAreaId = area.Id;
                RecalculateStats();
                MarkDirty();
                return true;
            }
        }
        return false;
    }

    public bool Zhai()
    {
        if (ActionVal < 2) return false;
        ActionVal -= 2;
        Player.HeroMgr.RestoreAllFatigue(5);
        MarkDirty();
        return true;
    }

    public void PassAreaStage(int areaId, int stageId)
    {
        AreaState? area = Comp.Areas.FirstOrDefault(a => a.Id == areaId);
        if (area is null) return;

        // 如果是高校学园通关 1106
        if (areaId == 2 && stageId == 1106)
        {
            area.Status = 0; // FREE
            area.CurrentStage = 0;

            // 解锁东方古街 (Area 3) 与 中央城区 (Area 4)
            AreaState? a3 = Comp.Areas.FirstOrDefault(a => a.Id == 3);
            if (a3 is not null) { a3.Status = 1; a3.Lock = false; }
            AreaState? a4 = Comp.Areas.FirstOrDefault(a => a.Id == 4);
            if (a4 is not null) { a4.Status = 1; a4.Lock = false; }
        }
        else
        {
            area.CurrentStage = stageId + 1;
        }

        MarkDirty();
    }
}
