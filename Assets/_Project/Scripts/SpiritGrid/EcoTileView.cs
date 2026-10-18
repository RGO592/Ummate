using System;

// Which orthogonal neighbours hold the same tile type. Diagonals never connect.
[Flags]
public enum EcoNeighborMask
{
    None = 0,
    North = 1,  // +Y (world +Z)
    East = 2,   // +X
    South = 4,  // -Y
    West = 8    // -X
}

// Implemented by tile visuals (placeholder or real prefab) that react to neighbours and build progress.
// A real model prefab can implement this to swap edge / corner pieces like an autotile.
public interface IEcoTileView
{
    void SetNeighbors(EcoNeighborMask mask);
    void SetBuildState(BuildState state);
}
