using System;
using UnityEngine;

// One ecosystem pattern on the eco grid: which environment tiles, at which relative Hex positions.
// Matching only reads the grid, so one Hex can belong to any number of habitats.
[CreateAssetMenu(fileName = "Habitat", menuName = "Ummate/Habitat")]
public sealed class HabitatDefinition : ScriptableObject
{
    [Serializable]
    public struct RequiredTile
    {
        public EnvironmentTileType tile;
        [Tooltip("Axial position relative to the pattern's anchor (0,0).")]
        public HexCoord offset;
    }

    [SerializeField, Tooltip("Stable save and event key. Never reuse or change after release.")]
    private string id;
    [SerializeField] private string displayName;
    [SerializeField] private RequiredTile[] requiredTiles = new RequiredTile[0];
    [SerializeField, Tooltip("Also match the pattern turned in 60 degree steps.")]
    private bool allowRotation = true;
    [SerializeField, Tooltip("Optional. While this quest is running, the first discovery is not revealed until it reaches the objective below. The pattern itself is still judged in any order.")]
    private string revealQuestId;
    [SerializeField] private string revealObjectiveId;

    public string Id => id;
    public string DisplayName => displayName;
    public string RevealQuestId => revealQuestId;
    public string RevealObjectiveId => revealObjectiveId;

    public bool Matches(EcoGridManager grid)
    {
        if (grid == null || requiredTiles == null || requiredTiles.Length == 0) return false;
        int rotations = allowRotation ? 6 : 1;
        foreach (HexCoord anchor in grid.CellCoordinates)
            for (int rotation = 0; rotation < rotations; rotation++)
                if (MatchesAt(grid, anchor, rotation)) return true;
        return false;
    }

    private bool MatchesAt(EcoGridManager grid, HexCoord anchor, int rotation)
    {
        foreach (RequiredTile required in requiredTiles)
        {
            EcoGridCell cell = grid.GetCell(anchor + required.offset.Rotate60(rotation));
            if (cell == null || cell.IsEmpty || cell.TileType != required.tile) return false;
        }
        return true;
    }
}
