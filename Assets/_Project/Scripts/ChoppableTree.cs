using UnityEngine;
using UnityEngine.Events;

// Only dedicated village trees carry this component. Decorative/boundary trees are untouched.
[DisallowMultipleComponent]
public sealed class ChoppableTree : Interactable
{
    [SerializeField, Min(1)] private int requiredHits = 3;
    [SerializeField] private ItemDefinition woodItem;
    [SerializeField, Min(1)] private int woodAmount = 3;
    [SerializeField] private GameObject visual;
    [SerializeField] private Collider[] treeColliders = new Collider[0];
    [SerializeField, Tooltip("Optional hook for a future hit animation or effect.")]
    private UnityEvent onHit = new UnityEvent();
    [SerializeField] private UnityEvent onChopped = new UnityEvent();
    private ItemDefinition requiredTool;
    private int completedHits;
    private bool isChopped;
    private bool interacting;

    public bool IsChopped => isChopped;
    public int CompletedHits => completedHits;
    public int RequiredHits => Mathf.Max(1, requiredHits);
    public int RemainingHits => isChopped ? 0 : Mathf.Max(0, RequiredHits - completedHits);
    public UnityEvent Chopped => onChopped;

    // Optional: this item must be in the inventory to chop. Scene trees leave it empty.
    public void RequireTool(ItemDefinition tool) => requiredTool = tool;

    public override string GetInteractionPrompt(GameObject interactor) => isChopped ? null : "나무 베기";

    public override void Interact(GameObject interactor)
    {
        if (interacting || isChopped || !isActiveAndEnabled || interactor == null) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null) return;
        if (woodItem == null || visual == null || treeColliders == null || treeColliders.Length == 0)
        {
            Debug.LogWarning($"{name}: 목재·Visual·Collider 연결을 확인해 주세요.", this);
            return;
        }
        if (requiredTool != null && inventory.GetQuantity(requiredTool) < 1)
        {
            PlayerFeedback.Notify($"{requiredTool.DisplayName}가 필요합니다.");
            return;
        }
        interacting = true;
        try
        {
            int nextHits = Mathf.Min(RequiredHits, completedHits + 1);
            if (nextHits < RequiredHits)
            {
                completedHits = nextHits;
                Debug.Log($"나무를 베는 중... {RemainingHits}", this);
                PlayerFeedback.Notify($"나무를 베는 중... {RemainingHits}");
                onHit?.Invoke();
                return;
            }
            int amount = Mathf.Max(1, woodAmount);
            if (inventory.GetQuantity(woodItem) > int.MaxValue - amount)
            {
                Debug.LogWarning("목재를 더 받을 수 없습니다. 나무는 마지막 작업 전 상태로 유지됩니다.", this);
                PlayerFeedback.Notify("목재를 더 받을 수 없습니다.");
                return;
            }
            int previousHits = completedHits;
            completedHits = nextHits;
            isChopped = true;
            ApplyChoppedVisual();
            // Commit the tree first: synchronous inventory listeners cannot re-enter and grant twice.
            // If a listener throws after AddItem committed, retain the chopped state and the reward.
            if (!inventory.AddItem(woodItem, amount))
            {
                completedHits = previousHits;
                isChopped = false;
                ApplyChoppedVisual();
                return;
            }
            Debug.Log($"목재 {amount}개를 획득했습니다. (보유 목재: {inventory.GetQuantity(woodItem)}개)", this);
            PlayerFeedback.Notify($"목재 {amount}개를 획득했습니다.");
            QuestEvents.Raise(QuestEventType.TreeChopped, woodItem);
            onHit?.Invoke();
            onChopped?.Invoke();
        }
        finally { interacting = false; }
    }

    private void ApplyChoppedVisual()
    {
        if (visual != null) visual.SetActive(!isChopped);
        if (treeColliders != null)
            foreach (Collider collider in treeColliders) if (collider != null) collider.enabled = !isChopped;
        enabled = !isChopped;
    }

    // Future regrowth can call this explicitly. No automatic day subscription or save schema here.
    public void ResetForRegrowth()
    {
        if (interacting) return;
        completedHits = 0;
        isChopped = false;
        ApplyChoppedVisual();
    }
}
