using UnityEngine;

// Re-evaluates this region's habitats whenever a cell changes (placement, move, removal).
// The first real completion is reported once; loading only re-reads the grid and reports nothing.
// Subscribed for its lifetime, not while enabled: a load can happen while this scene is suspended.
[DisallowMultipleComponent]
public sealed class HabitatTracker : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private HabitatDefinition[] habitats = new HabitatDefinition[0];

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

    private void OnCellChanged(EcoGridCell cell)
    {
        foreach (HabitatDefinition habitat in habitats)
        {
            if (!Evaluate(habitat)) continue;
            // A held discovery stays unrecorded, so the cell change that reaches the objective reveals it.
            if (!IsRevealHeld(habitat) && HabitatRecords.TryDiscover(habitat.Id))
            {
                Debug.Log($"생태계 최초 발견: {habitat.Id} {habitat.DisplayName}", this);
                QuestEvents.Raise(QuestEventType.HabitatCompleted, null, habitat.Id);
                PlayerFeedback.Notify($"{habitat.DisplayName} 생태계를 발견했습니다.");
            }
            QuestEvents.Raise(QuestEventType.HabitatPatternStanding, null, habitat.Id);
        }
    }

    private bool IsRevealHeld(HabitatDefinition habitat)
    {
        QuestLog quests = grid.Quests;
        return quests != null && !string.IsNullOrEmpty(habitat.RevealQuestId)
            && quests.IsBeforeObjective(habitat.RevealQuestId, habitat.RevealObjectiveId);
    }

    private void RefreshSilently()
    {
        foreach (HabitatDefinition habitat in habitats) Evaluate(habitat);
    }

    private bool Evaluate(HabitatDefinition habitat)
    {
        if (habitat == null) return false;
        grid.EnsureGrid();
        bool complete = habitat.Matches(grid);
        HabitatRecords.SetComplete(habitat.Id, complete);
        return complete;
    }
}
