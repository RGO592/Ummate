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

    // Once ever per habitat, on the first real completion; loading never raises it.
    // The hook for later presentation (spirit, animal, effects).
    public static event Action<string> FirstDiscovered;
    // After a load or new game replaced the records, so live trackers can re-read their grids.
    public static event Action Restored;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        discovered.Clear();
        complete.Clear();
        FirstDiscovered = null;
        Restored = null;
    }

    public static bool IsDiscovered(string id) => discovered.Contains(id);
    // The pattern currently stands on the grid. Discovery stays even if it is later taken apart.
    public static bool IsComplete(string id) => complete.Contains(id);

    public static void SetComplete(string id, bool isComplete)
    {
        if (isComplete) complete.Add(id);
        else complete.Remove(id);
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
    public static void Restore(GameSaveData.HabitatState[] saved)
    {
        discovered.Clear();
        complete.Clear();
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
