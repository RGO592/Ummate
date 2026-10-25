using UnityEngine;

// Forest village trading post. F opens the sell UI; no NPC dialogue.
[DisallowMultipleComponent]
public sealed class TradingPost : Interactable
{
    [SerializeField] private TradingPostUI ui;

    public override string GetInteractionPrompt(GameObject interactor) => ui != null ? "교역소 이용" : null;

    public override void Interact(GameObject interactor)
    {
        if (interactor == null) return;
        if (ui == null)
        {
            Debug.LogWarning($"{name}: TradingPostUI 연결을 확인해 주세요.", this);
            return;
        }
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        PlayerWallet wallet = interactor.GetComponent<PlayerWallet>();
        if (inventory == null || wallet == null)
        {
            Debug.LogWarning($"{name}: 플레이어의 PlayerInventory / PlayerWallet을 찾을 수 없습니다.", this);
            return;
        }
        ui.Open(inventory, wallet);
    }
}
