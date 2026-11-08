using System;
using System.Collections.Generic;
using UnityEngine;

// One ecosystem on the eco grid: how many Hexes of which environment it needs. The habitat stands when those
// Hexes form one connected group (pointy-top axial, 6 neighbours, HexCoord.Directions) in any shape, placed in
// any order; the same counts scattered over the map do not count. Matching only reads the grid, so one Hex can
// belong to any number of habitats at once (one Flower can serve Bee and Rabbit).
[CreateAssetMenu(fileName = "Habitat", menuName = "Susupjung/Habitat")]
public sealed class HabitatDefinition : ScriptableObject
{
    [Serializable]
    public struct RequiredCount
    {
        public EnvironmentTileType tile;
        [Min(1)] public int count;
    }

    [SerializeField, Tooltip("Stable save and event key. Never reuse or change after release.")]
    private string id;
    [SerializeField] private string displayName;
    [SerializeField, Tooltip("Exactly these Hexes, all connected to each other. Other Hexes next to the group do not matter.")]
    private RequiredCount[] requiredCounts = new RequiredCount[0];
    [SerializeField, Tooltip("Optional. While this quest is running, the first discovery is not revealed until it reaches the objective below. The pattern itself is still judged in any order.")]
    private string revealQuestId;
    [SerializeField] private string revealObjectiveId;

    public string Id => id;
    public string DisplayName => displayName;
    public string RevealQuestId => revealQuestId;
    public string RevealObjectiveId => revealObjectiveId;

    public bool Matches(EcoGridManager grid)
    {
        if (grid == null || requiredCounts == null) return false;
        var remaining = new Dictionary<EnvironmentTileType, int>();
        int total = 0;
        foreach (RequiredCount required in requiredCounts)
        {
            if (required.tile == EnvironmentTileType.None || required.count <= 0) continue;
            remaining.TryGetValue(required.tile, out int current);
            remaining[required.tile] = current + required.count;
            total += required.count;
        }
        if (total == 0) return false;
        var group = new List<HexCoord>(total);
        foreach (HexCoord start in grid.CellCoordinates)
        {
            if (!TryTake(grid, start, remaining, group)) continue;
            if (Grow(grid, remaining, group, total)) return true;
            Release(grid, remaining, group);
        }
        return false;
    }

    // Depth-first over connected sets: add one neighbour of the group at a time while its type is still needed.
    // A habitat needs only a few Hexes, so the search stays tiny. Leaves the group as it found it on failure.
    private static bool Grow(EcoGridManager grid, Dictionary<EnvironmentTileType, int> remaining, List<HexCoord> group, int total)
    {
        if (group.Count == total) return true;
        for (int g = 0; g < group.Count; g++)
            for (int d = 0; d < HexCoord.Directions.Count; d++)
            {
                HexCoord next = group[g].Neighbor(d);
                if (group.Contains(next) || !TryTake(grid, next, remaining, group)) continue;
                if (Grow(grid, remaining, group, total)) return true;
                Release(grid, remaining, group);
            }
        return false;
    }

    private static bool TryTake(EcoGridManager grid, HexCoord coord, Dictionary<EnvironmentTileType, int> remaining, List<HexCoord> group)
    {
        EcoGridCell cell = grid.GetCell(coord);
        if (cell == null || cell.IsEmpty || !remaining.TryGetValue(cell.TileType, out int left) || left <= 0) return false;
        remaining[cell.TileType] = left - 1;
        group.Add(coord);
        return true;
    }

    // Undoes the last TryTake.
    private static void Release(EcoGridManager grid, Dictionary<EnvironmentTileType, int> remaining, List<HexCoord> group)
    {
        HexCoord last = group[group.Count - 1];
        group.RemoveAt(group.Count - 1);
        EnvironmentTileType type = grid.GetCell(last).TileType;
        remaining[type] += 1;
    }
}
