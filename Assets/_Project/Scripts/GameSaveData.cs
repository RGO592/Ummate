using System;
using UnityEngine;

// Plain JSON data only. IDs are assigned in the scene, not taken from runtime instance IDs.
[Serializable]
public sealed class GameSaveData
{
    public int version;
    public int day;
    public int selectedSlot;
    // Added after v1 shipped. JsonUtility leaves it 0 when an older save has no "gold" field.
    public int gold;
    public ItemState[] inventory;
    public MissionState mission;
    public PlotState[] plots;
    public ChickenState[] chickens;
    public CowState[] cows;
    // Added after v1 shipped. Older saves have no field (null) and load as empty grids.
    public HexGridState[] hexGrids;
    // Added after v1 shipped. Older saves have no field (null) and load with every flower bed empty.
    public FlowerBedState[] flowerBeds;
    // Added after v1 shipped: where the player was (Build Settings scene path + player root position).
    // Older saves have no scene (empty) and continue in FarmScene where its player already stands.
    public string scene;
    public Vector3 playerPosition;
    // Added after v1 shipped. Older saves have no field (null) and new games write none (empty):
    // both start the quest line from its first quest. Quests missing from a save load as Locked.
    public QuestSaveState[] quests;
    // Added after v1 shipped. Older saves and scenes without village build mode have none: nothing is placed.
    public VillageFacilityState[] villageFacilities;
    // Added after v1 shipped. Older saves have no field (null): nothing has been discovered.
    public HabitatState[] habitats;

    // completed = the pattern stood on the grid when saved; re-read from the grid after loading.
    [Serializable] public sealed class HabitatState
    {
        public string id;
        public bool discovered, completed;
    }
    // One placed village facility. Only the block matching its prefab's component is read.
    [Serializable] public sealed class VillageFacilityState
    {
        public string definitionId;
        public int x, y; // Footprint's minimum cell; the size comes from the definition and rotation.
        public int rotationSteps; // 0-3, 90 degrees each. Older saves have no field and load as 0.
        public PlotState plot;
        public FlowerBedState flowerBed;
        public TreePlotState treePlot;
    }
    [Serializable] public sealed class TreePlotState
    {
        public TreePlot.PlotStage stage;
        public int growthDays;
    }
    [Serializable] public sealed class QuestSaveState
    {
        public string id;
        public QuestStatus state;
        public ObjectiveSaveState[] objectives;
    }
    [Serializable] public sealed class ObjectiveSaveState { public string id; public int progress; }
    [Serializable] public sealed class ItemState { public string id; public int quantity; }
    // The state covers planted / watered / harvestable.
    [Serializable] public sealed class FlowerBedState
    {
        public string id;
        public FlowerBed.BedState state;
    }
    // One entry per region Hex grid (EcoGridManager.SaveId). Only placed cells are stored; background hexes never.
    [Serializable] public sealed class HexGridState
    {
        public string id;
        public HexCellState[] cells;
    }
    [Serializable] public sealed class HexCellState
    {
        public int q, r;
        public EnvironmentTileType tileType;
    }
    // Legacy ranch prologue (M01–M03). Still written and validated so existing saves keep loading;
    // MissionProgress no longer advances it (see legacyMissionsEnabled).
    [Serializable] public sealed class MissionState
    {
        public int stage;
        public int wheatHarvested, eggsCollected, cornHarvested, milkCollected;
        // Existing completion flags are set only after the corresponding reward is granted.
        public bool m01CompletedAndRewarded, m02CompletedAndRewarded, m03CompletedAndRewarded;
        public bool prologueCompleted;
        public bool chickenUnlocked, cowUnlocked;
    }
    [Serializable] public sealed class PlotState
    {
        public string id;
        public FarmPlot.CropKind crop;
        public FarmPlot.CropState state;
        public int growthDays;
        public bool wateredToday;
    }
    [Serializable] public sealed class ChickenState
    {
        public string id;
        public bool fedToday, eggAvailable;
    }
    [Serializable] public sealed class CowState
    {
        public string id;
        public bool fedToday, milkReady;
    }
}
