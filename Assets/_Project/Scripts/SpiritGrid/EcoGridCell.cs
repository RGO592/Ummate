using UnityEngine;

// Runtime data for one grid cell. Coordinates: pointy-top axial q/r.
// Placed tiles are complete immediately; there is no construction state.
public sealed class EcoGridCell
{
    public HexCoord Coord { get; }
    public EnvironmentTileType TileType { get; private set; } = EnvironmentTileType.None;
    public GameObject Visual { get; private set; }

    public bool IsEmpty => TileType == EnvironmentTileType.None;

    public EcoGridCell(HexCoord coord) => Coord = coord;

    internal void SetTile(EnvironmentTileType type, GameObject visual)
    {
        TileType = type;
        Visual = visual;
    }

    // Moves tile and visual from another cell into this one.
    internal void TakeFrom(EcoGridCell source)
    {
        TileType = source.TileType;
        Visual = source.Visual;
        source.Clear();
    }

    internal void Clear()
    {
        TileType = EnvironmentTileType.None;
        Visual = null;
    }
}
