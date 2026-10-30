using System;
using UnityEngine;

// Until the trial quest is done, the trading post refuses to sell the last seeds the trial still needs.
// Only that one case is blocked: other items, extra seeds, or seeds once a crop of that kind is already
// in a plot all sell normally. Lives on the FarmScene player and registers in Awake (scene suspension
// must not drop it), so it reads inventory and plots only when a sale is attempted.
// Kept materials work the same way: the last units a later objective needs stay unsold until it is done.
[DisallowMultipleComponent]
public sealed class QuestSeedSaleGuard : MonoBehaviour
{
    [Serializable]
    public sealed class KeptMaterial
    {
        public ItemDefinition item;
        [Min(1)] public int keepAtLeast = 1;
        [Tooltip("Protection ends once this objective is done.")]
        public string questId;
        public string objectiveId;
    }

    [SerializeField] private QuestLog quests;
    [SerializeField, Tooltip("Protection ends once this quest is Completed.")]
    private string protectUntilQuestId = "Q01";
    [SerializeField] private ItemDefinition protectedSeeds;
    [SerializeField, Tooltip("A plot already growing this crop means the trial no longer needs a seed.")]
    private FarmPlot.CropKind crop = FarmPlot.CropKind.Wheat;
    [SerializeField, Min(1)] private int keepAtLeast = 1;
    [SerializeField, TextArea] private string blockedMessage = "안 돼, 안 돼! 이거까지 팔면 시험을 통과할 수 없어!";
    [SerializeField] private KeptMaterial[] keptMaterials = new KeptMaterial[0];
    [SerializeField, TextArea] private string materialBlockedMessage = "시험에 필요한 재료는 남겨 두자!";

    private void Awake() => TradeService.AddSaleBlocker(Check);
    private void OnDestroy() => TradeService.RemoveSaleBlocker(Check);

    private string Check(PlayerInventory inventory, ItemDefinition item, int quantity)
    {
        if (item == null || quests == null || inventory == null) return null;
        foreach (KeptMaterial kept in keptMaterials)
        {
            if (kept == null || kept.item != item || quests.IsObjectiveDone(kept.questId, kept.objectiveId)) continue;
            if ((long)inventory.GetQuantity(item) - quantity >= kept.keepAtLeast) continue;
            PlayerFeedback.Notify(materialBlockedMessage);
            return materialBlockedMessage;
        }
        if (item != protectedSeeds) return null;
        if (quests.GetStatus(protectUntilQuestId) == QuestStatus.Completed) return null;
        // Found on each attempt so plots placed in village build mode count too.
        foreach (FarmPlot plot in FindObjectsByType<FarmPlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (plot.CurrentCrop == crop && plot.State != FarmPlot.CropState.Empty) return null;
        if ((long)inventory.GetQuantity(item) - quantity >= keepAtLeast) return null;
        PlayerFeedback.Notify(blockedMessage);
        return blockedMessage;
    }
}
