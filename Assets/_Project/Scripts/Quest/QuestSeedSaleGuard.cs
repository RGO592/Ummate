using UnityEngine;

// Until the trial quest is done, the trading post refuses to sell the last seeds the trial still needs.
// Only that one case is blocked: other items, extra seeds, or seeds once a crop of that kind is already
// in a plot all sell normally. Lives on the FarmScene player and registers in Awake (scene suspension
// must not drop it), so it reads inventory and plots only when a sale is attempted.
[DisallowMultipleComponent]
public sealed class QuestSeedSaleGuard : MonoBehaviour
{
    [SerializeField] private QuestLog quests;
    [SerializeField, Tooltip("Protection ends once this quest is Completed.")]
    private string protectUntilQuestId = "Q01";
    [SerializeField] private ItemDefinition protectedSeeds;
    [SerializeField, Tooltip("A plot already growing this crop means the trial no longer needs a seed.")]
    private FarmPlot.CropKind crop = FarmPlot.CropKind.Wheat;
    [SerializeField] private FarmPlot[] plots = new FarmPlot[0];
    [SerializeField, Min(1)] private int keepAtLeast = 1;
    [SerializeField, TextArea] private string blockedMessage = "안 돼, 안 돼! 이거까지 팔면 시험을 통과할 수 없어!";

    private void Awake() => TradeService.AddSaleBlocker(Check);
    private void OnDestroy() => TradeService.RemoveSaleBlocker(Check);

    private string Check(PlayerInventory inventory, ItemDefinition item, int quantity)
    {
        if (item == null || item != protectedSeeds || quests == null || inventory == null) return null;
        if (quests.GetStatus(protectUntilQuestId) == QuestStatus.Completed) return null;
        foreach (FarmPlot plot in plots)
            if (plot != null && plot.CurrentCrop == crop && plot.State != FarmPlot.CropState.Empty) return null;
        if ((long)inventory.GetQuantity(item) - quantity >= keepAtLeast) return null;
        PlayerFeedback.Notify(blockedMessage);
        return blockedMessage;
    }
}
