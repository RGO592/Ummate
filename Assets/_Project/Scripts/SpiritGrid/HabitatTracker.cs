using System;
using System.Collections.Generic;
using UnityEngine;

// Re-evaluates this region's habitats (fixed shapes, HabitatDefinition.pattern) whenever a cell changes.
// Discovery happens only through a player's Hex change, and at most one habitat per action:
//   - candidates: not yet discovered, not held for a quest, and a placement of the shape that uses the changed Hex
//     (a shape that was already standing elsewhere is not discovered by an unrelated change);
//   - one per frame (a move reports its source and target cells; neither may add a second discovery);
//   - pick: wanted by a running quest, then the higher priority, then the id (ordinal).
// Loading only re-reads the grid: it records what stands (for animals and Q06) but never discovers anything.
// Subscribed for its lifetime, not while enabled: a load can happen while this scene is suspended.
[DisallowMultipleComponent]
public sealed class HabitatTracker : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private HabitatDefinition[] habitats = new HabitatDefinition[0];

    private readonly List<HexCoord> group = new List<HexCoord>();
    private readonly List<HabitatDefinition> candidates = new List<HabitatDefinition>();
    private int lastDiscoveryFrame = -1;

    private void Awake()
    {
        if (grid == null)
        {
            Debug.LogWarning($"{name}: EcoGridManager가 연결되지 않아 생태계를 판정하지 않습니다.", this);
            return;
        }
        grid.CellChanged += OnCellChanged;
        grid.GridRestored += RefreshSilently;
        HabitatRecords.Restored += RefreshSilently;
    }

    // A grid restored in its own Awake may have finished before this subscribed.
    private void Start() => RefreshSilently();

    private void OnDestroy()
    {
        if (grid == null) return;
        grid.CellChanged -= OnCellChanged;
        grid.GridRestored -= RefreshSilently;
        HabitatRecords.Restored -= RefreshSilently;
    }

    private void OnCellChanged(EcoGridCell changed)
    {
        // What stands now (animals live on these cells; Q06's last objective listens for its pattern).
        RefreshSilently();

        if (changed != null && lastDiscoveryFrame != Time.frameCount)
        {
            candidates.Clear();
            foreach (HabitatDefinition habitat in habitats)
                if (habitat != null && !HabitatRecords.IsDiscovered(habitat.Id) && !IsRevealHeld(habitat) &&
                    (habitat.TryFindGroupContaining(grid, changed.Coord, null) ||
                     // A quest-held habitat built early is revealed by the change that reaches its objective.
                     (!string.IsNullOrEmpty(habitat.RevealQuestId) && HabitatRecords.IsComplete(habitat.Id))))
                    candidates.Add(habitat);
            if (candidates.Count > 0)
            {
                QuestLog quests = grid.Quests;
                candidates.Sort((a, b) =>
                {
                    bool wantA = quests != null && quests.WantsHabitat(a.Id), wantB = quests != null && quests.WantsHabitat(b.Id);
                    if (wantA != wantB) return wantA ? -1 : 1;
                    if (a.Priority != b.Priority) return b.Priority.CompareTo(a.Priority);
                    return string.CompareOrdinal(a.Id, b.Id);
                });
                HabitatDefinition found = candidates[0];
                if (HabitatRecords.TryDiscover(found.Id))
                {
                    lastDiscoveryFrame = Time.frameCount;
                    Debug.Log($"생태계 최초 발견: {found.Id} {found.DisplayName}", this);
                    QuestEvents.Raise(QuestEventType.HabitatCompleted, null, found.Id);
                    PlayerFeedback.Notify($"{found.DisplayName} 생태계를 발견했습니다.");
                }
            }
        }

        foreach (HabitatDefinition habitat in habitats)
            if (habitat != null && HabitatRecords.IsComplete(habitat.Id))
                QuestEvents.Raise(QuestEventType.HabitatPatternStanding, null, habitat.Id);
    }

    private bool IsRevealHeld(HabitatDefinition habitat)
    {
        QuestLog quests = grid.Quests;
        return quests != null && !string.IsNullOrEmpty(habitat.RevealQuestId)
            && quests.IsBeforeObjective(habitat.RevealQuestId, habitat.RevealObjectiveId);
    }

    private void RefreshSilently()
    {
        foreach (HabitatDefinition habitat in habitats)
        {
            if (habitat == null) continue;
            grid.EnsureGrid();
            bool complete = habitat.TryFindGroup(grid, group);
            HabitatRecords.SetComplete(habitat.Id, complete, group);
        }
    }
}
