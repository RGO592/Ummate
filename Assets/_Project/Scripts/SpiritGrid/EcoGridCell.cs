using UnityEngine;

public enum BuildState
{
    Empty,
    Constructing,
    Complete
}

// Runtime data for one grid cell. Coordinates: X = grid column (world X), Y = grid row (world Z).
public sealed class EcoGridCell
{
    public Vector2Int Coord { get; }
    public EnvironmentTileType TileType { get; private set; } = EnvironmentTileType.None;
    public BuildState State { get; private set; } = BuildState.Empty;
    public int RemainingBuildDays { get; private set; }
    public GameObject Visual { get; private set; }
    // Grey base slab drawn under the cell; owned by the manager.
    public GameObject BaseVisual { get; internal set; }

    public bool IsEmpty => State == BuildState.Empty;
    public bool IsComplete => State == BuildState.Complete;

    public EcoGridCell(Vector2Int coord) => Coord = coord;

    internal void SetTile(EnvironmentTileType type, int buildDays, GameObject visual)
    {
        TileType = type;
        RemainingBuildDays = Mathf.Max(0, buildDays);
        State = RemainingBuildDays > 0 ? BuildState.Constructing : BuildState.Complete;
        Visual = visual;
    }

    // Moves tile, build progress and visual from another cell into this one.
    internal void TakeFrom(EcoGridCell source)
    {
        TileType = source.TileType;
        State = source.State;
        RemainingBuildDays = source.RemainingBuildDays;
        Visual = source.Visual;
        source.Clear();
    }

    // Returns true when this call finished construction.
    internal bool AdvanceBuildDay()
    {
        if (State != BuildState.Constructing) return false;
        RemainingBuildDays = Mathf.Max(0, RemainingBuildDays - 1);
        if (RemainingBuildDays > 0) return false;
        State = BuildState.Complete;
        return true;
    }

    internal void Clear()
    {
        TileType = EnvironmentTileType.None;
        State = BuildState.Empty;
        RemainingBuildDays = 0;
        Visual = null;
    }
}
