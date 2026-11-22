using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// One ecosystem on the eco grid as a fixed shape: each pattern cell is an environment at an axial offset from the
// anchor (0,0). It stands only where the grid holds exactly that shape: every pattern cell present with its tile.
// The same kinds in another arrangement do not count. Other Hexes around the shape do not matter, and one Hex can
// belong to several habitats at once. The shape may be turned in 60° steps (Allow Rotation) and flipped left-right
// (Allow Mirror), like a habitat card laid in any of its orientations; it may never be reshaped.
[CreateAssetMenu(fileName = "Habitat", menuName = "Susupjung/Habitat")]
public sealed class HabitatDefinition : ScriptableObject
{
    [Serializable]
    public struct PatternCell
    {
        public EnvironmentTileType tile;
        [Tooltip("Axial position relative to the anchor (0,0).")]
        public HexCoord offset;
    }

    [SerializeField, Tooltip("Stable save and event key. Never reuse or change after release.")]
    private string id;
    [SerializeField] private string displayName;
    [SerializeField, Tooltip("The shape: one entry per Hex. The first cell is a convenient anchor (0,0).")]
    private PatternCell[] pattern = new PatternCell[0];
    [SerializeField, Tooltip("Also match the shape turned in 60 degree steps.")]
    private bool allowRotation = true;
    [SerializeField, Tooltip("Also match the shape flipped left-right (and that flip turned, with Allow Rotation).")]
    private bool allowMirror = true;
    [SerializeField, Tooltip("When one action completes several new habitats, only one is discovered: quest-related first, then the higher priority, then the id.")]
    private int priority;
    [SerializeField, Tooltip("Optional. While this quest is running, the first discovery is not revealed until it reaches the objective below. The pattern itself is still judged in any order.")]
    private string revealQuestId;
    [SerializeField] private string revealObjectiveId;

    public string Id => id;
    public string DisplayName => displayName;
    public int Priority => priority;
    public bool AllowRotation => allowRotation;
    public bool AllowMirror => allowMirror;
    public IReadOnlyList<PatternCell> Pattern => pattern ?? new PatternCell[0];
    public string RevealQuestId => revealQuestId;
    public string RevealObjectiveId => revealObjectiveId;

    // Orientations: 0–5 the shape turned in 60° steps, 6–11 the left-right flipped shape turned the same way.
    private int Orientations => (allowRotation ? 6 : 1) * (allowMirror ? 2 : 1);

    // Axial (q, r) → (r, q) swaps two cube axes: a reflection. The flipped orientations use it before turning.
    private int Turns => allowRotation ? 6 : 1;
    private HexCoord Orient(HexCoord offset, int orientation)
    {
        if (orientation >= Turns) offset = new HexCoord(offset.r, offset.q);
        return offset.Rotate60(orientation % Turns);
    }

    public bool Matches(EcoGridManager grid) => TryFindGroup(grid, null);

    // Any placement of the shape on the grid. `found` (cleared first, may be null) receives its Hexes.
    public bool TryFindGroup(EcoGridManager grid, List<HexCoord> found)
    {
        found?.Clear();
        if (grid == null || pattern == null || pattern.Length == 0) return false;
        foreach (HexCoord anchor in grid.CellCoordinates)
            for (int orientation = 0; orientation < Orientations; orientation++)
                if (MatchesAt(grid, anchor, orientation, found)) return true;
        return false;
    }

    // A placement of the shape that uses this Hex (the one a player action just changed).
    public bool TryFindGroupContaining(EcoGridManager grid, HexCoord coord, List<HexCoord> found)
    {
        found?.Clear();
        if (grid == null || pattern == null || pattern.Length == 0) return false;
        for (int orientation = 0; orientation < Orientations; orientation++)
            foreach (PatternCell cell in pattern)
            {
                HexCoord placed = Orient(cell.offset, orientation);
                HexCoord anchor = new HexCoord(coord.q - placed.q, coord.r - placed.r);
                if (MatchesAt(grid, anchor, orientation, found)) return true;
            }
        return false;
    }

    private bool MatchesAt(EcoGridManager grid, HexCoord anchor, int orientation, List<HexCoord> found)
    {
        foreach (PatternCell required in pattern)
        {
            EcoGridCell cell = grid.GetCell(anchor + Orient(required.offset, orientation));
            if (cell == null || cell.IsEmpty || cell.TileType != required.tile) return false;
        }
        if (found != null)
        {
            found.Clear();
            foreach (PatternCell required in pattern) found.Add(anchor + Orient(required.offset, orientation));
        }
        return true;
    }

    // "꽃 1 + 버섯 1": the kinds in pattern order with their counts, for the journal.
    public string RequirementText
    {
        get
        {
            var order = new List<EnvironmentTileType>();
            var counts = new Dictionary<EnvironmentTileType, int>();
            foreach (PatternCell cell in Pattern)
            {
                if (cell.tile == EnvironmentTileType.None) continue;
                if (!counts.ContainsKey(cell.tile)) { order.Add(cell.tile); counts[cell.tile] = 0; }
                counts[cell.tile]++;
            }
            var text = new StringBuilder();
            foreach (EnvironmentTileType tile in order)
            {
                if (text.Length > 0) text.Append(" + ");
                text.Append(EnvironmentTileTypes.DisplayName(tile)).Append(' ').Append(counts[tile]);
            }
            return text.ToString();
        }
    }
}
