using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Habitat state for the whole game, independent of any scene: the save system lives in the village
// scene and can save or load while the region's grid is suspended or not loaded yet.
public static class HabitatRecords
{
    private static readonly HashSet<string> discovered = new HashSet<string>();
    private static readonly HashSet<string> complete = new HashSet<string>();
    // Runtime only (re-read from the grid after every change and load): the Hexes that currently form each
    // standing habitat. Its animal lives on exactly these cells.
    private static readonly Dictionary<string, HexCoord[]> groups = new Dictionary<string, HexCoord[]>();
    private static readonly HexCoord[] NoCells = new HexCoord[0];

    // Once ever per habitat, on the first real completion; loading never raises it.
    // The hook for later presentation (spirit, animal, effects). The habitat's cells are already set when it fires.
    public static event Action<string> FirstDiscovered;
    // After a load or new game replaced the records, so live trackers can re-read their grids.
    public static event Action Restored;
    // The cells forming a habitat changed (built, moved, taken apart, re-read after a load).
    public static event Action<string> GroupChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        discovered.Clear();
        complete.Clear();
        groups.Clear();
        FirstDiscovered = null;
        Restored = null;
        GroupChanged = null;
    }

    public static bool IsDiscovered(string id) => discovered.Contains(id);
    // Every habitat revealed to the player so far (each id once). Read-only view for quest progress.
    public static IReadOnlyCollection<string> Discovered => discovered;
    // The pattern currently stands on the grid. Discovery stays even if it is later taken apart.
    public static bool IsComplete(string id) => complete.Contains(id);
    // Empty while the habitat does not stand.
    public static IReadOnlyList<HexCoord> CellsOf(string id) => groups.TryGetValue(id, out HexCoord[] cells) ? cells : NoCells;

    public static void SetComplete(string id, bool isComplete) => SetComplete(id, isComplete, null);

    public static void SetComplete(string id, bool isComplete, IReadOnlyCollection<HexCoord> cells)
    {
        if (isComplete) complete.Add(id);
        else complete.Remove(id);
        HexCoord[] next = isComplete && cells != null ? cells.ToArray() : NoCells;
        HexCoord[] previous = groups.TryGetValue(id, out HexCoord[] known) ? known : NoCells;
        if (previous.Length == next.Length && !previous.Except(next).Any()) return;
        if (next.Length > 0) groups[id] = next;
        else groups.Remove(id);
        try { GroupChanged?.Invoke(id); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    // True only the first time for this habitat.
    public static bool TryDiscover(string id)
    {
        if (!discovered.Add(id)) return false;
        try { FirstDiscovered?.Invoke(id); }
        catch (Exception exception) { Debug.LogException(exception); }
        return true;
    }

    public static GameSaveData.HabitatState[] Capture() => discovered.Union(complete).OrderBy(id => id, StringComparer.Ordinal)
        .Select(id => new GameSaveData.HabitatState { id = id, discovered = discovered.Contains(id), completed = complete.Contains(id) })
        .ToArray();

    // Load / new game. Null or empty (older saves, New Game) means nothing has been discovered.
    // The cells are not saved: the region grid restores its Hexes and the trackers re-read them.
    public static void Restore(GameSaveData.HabitatState[] saved)
    {
        discovered.Clear();
        complete.Clear();
        groups.Clear(); // Live trackers re-read them on Restored; a region loaded later reads them in Start.
        if (saved != null)
            foreach (GameSaveData.HabitatState state in saved)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.id)) continue;
                if (state.discovered) discovered.Add(state.id);
                if (state.completed) complete.Add(state.id);
            }
        try { Restored?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
}
