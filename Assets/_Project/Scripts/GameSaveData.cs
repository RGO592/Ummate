using System;

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

    [Serializable] public sealed class ItemState { public string id; public int quantity; }
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
