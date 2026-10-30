using UnityEngine;

// Village tree site. Stays placed through every cycle: Empty -> Sapling -> Mature -> (chopped) -> Empty.
// Chopping itself is the existing ChoppableTree, spawned as a child only while Mature.
// Values marked "임시" are prototype test values, not final balance.
[DisallowMultipleComponent]
public sealed class TreePlot : Interactable
{
    public enum PlotStage { Empty, Sapling, Mature }

    [SerializeField] private GameDay gameDay;
    [SerializeField] private ItemDefinition sapling;
    [SerializeField, Tooltip("Owned, not equipped: the mature tree can be chopped while this is in the inventory.")]
    private ItemDefinition axe;
    [SerializeField, Tooltip("ChoppableTree prefab spawned while Mature.")]
    private GameObject matureTreePrefab;
    [SerializeField] private GameObject saplingVisual;
    [SerializeField, Tooltip("Off while Mature, so F always targets the tree instead of this empty plot.")]
    private Collider plotCollider;
    [SerializeField, Min(1), Tooltip("심은 뒤 성목까지 걸리는 날짜 (임시 테스트 값).")]
    private int requiredGrowthDays = 1;

    private PlotStage stage;
    private int growthDays;
    private ChoppableTree tree;
    private bool interacting;

    public PlotStage Stage => stage;

    public GameSaveData.TreePlotState CaptureState() => new GameSaveData.TreePlotState { stage = stage, growthDays = growthDays };

    // Load / new game only. Never treated as a day passing.
    public void RestoreState(GameSaveData.TreePlotState saved)
    {
        growthDays = saved != null ? saved.growthDays : 0;
        SetStage(saved != null ? saved.stage : PlotStage.Empty);
    }

    // For instances spawned at runtime: prefabs cannot reference the scene's GameDay.
    public void BindGameDay(GameDay day)
    {
        if (isActiveAndEnabled && gameDay != null) gameDay.DayChanged -= OnDayChanged;
        gameDay = day;
        if (isActiveAndEnabled && gameDay != null) gameDay.DayChanged += OnDayChanged;
    }

    // Only GameDay.Sleep raises DayChanged: loading and scene travel never grow the sapling.
    private void OnEnable()
    {
        if (gameDay != null) gameDay.DayChanged += OnDayChanged;
    }

    private void OnDisable()
    {
        if (gameDay != null) gameDay.DayChanged -= OnDayChanged;
    }

    public override string GetInteractionPrompt(GameObject interactor) => stage == PlotStage.Empty ? "묘목 심기" : null;

    public override void Interact(GameObject interactor)
    {
        if (interacting || interactor == null) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null) return;
        if (sapling == null)
        {
            Debug.LogWarning($"{name}: 나무터의 묘목 연결을 확인해 주세요.", this);
            return;
        }
        // Inventory events must not trigger a second operation during the first one.
        interacting = true;
        try
        {
            if (stage == PlotStage.Sapling)
            {
                PlayerFeedback.Notify("묘목이 자라는 중입니다.");
                return;
            }
            if (stage != PlotStage.Empty) return;
            if (!inventory.TryConsume(sapling, 1))
            {
                PlayerFeedback.Notify("묘목이 없습니다.");
                return;
            }
            growthDays = 0;
            SetStage(PlotStage.Sapling);
            PlayerFeedback.Notify("묘목을 심었습니다.");
            QuestEvents.Raise(QuestEventType.SaplingPlanted, sapling);
        }
        finally { interacting = false; }
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        if (stage != PlotStage.Sapling) return;
        growthDays++;
        if (growthDays >= requiredGrowthDays) SetStage(PlotStage.Mature);
    }

    private void SetStage(PlotStage next)
    {
        stage = next;
        if (saplingVisual != null) saplingVisual.SetActive(stage == PlotStage.Sapling);
        if (plotCollider != null) plotCollider.enabled = stage != PlotStage.Mature;
        if (stage == PlotStage.Mature) SpawnTree();
        else RemoveTree();
    }

    private void SpawnTree()
    {
        if (tree != null || matureTreePrefab == null) return;
        GameObject instance = Instantiate(matureTreePrefab, transform.position, transform.rotation, transform);
        if (!instance.TryGetComponent(out tree))
        {
            Debug.LogWarning($"{name}: 성목 Prefab에 ChoppableTree가 없습니다.", this);
            Destroy(instance);
            return;
        }
        tree.RequireTool(axe);
        tree.Chopped.AddListener(OnTreeChopped);
    }

    private void RemoveTree()
    {
        if (tree == null) return;
        tree.Chopped.RemoveListener(OnTreeChopped);
        Destroy(tree.gameObject);
        tree = null;
    }

    // Wood, feedback and the quest event already came from ChoppableTree.
    private void OnTreeChopped()
    {
        growthDays = 0;
        SetStage(PlotStage.Empty);
    }
}
