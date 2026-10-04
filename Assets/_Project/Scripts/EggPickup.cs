using UnityEngine;

[DisallowMultipleComponent]
public sealed class EggPickup : Interactable
{
    [SerializeField] private ItemDefinition eggItem;
    [SerializeField] private ItemDefinition basket;
    private bool available;
    private bool collecting;
    public bool IsAvailable => available;

    // Temporary production rule: retain at most one uncollected egg per chicken.
    public bool Produce()
    {
        if (available) return false;
        available = true;
        gameObject.SetActive(true);
        return true;
    }

    public override void Interact(GameObject interactor)
    {
        if (!isActiveAndEnabled || !available || collecting || interactor == null) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        PlayerQuickbar quickbar = interactor.GetComponent<PlayerQuickbar>();
        if (inventory == null || quickbar == null) return;
        if (eggItem == null || basket == null)
        {
            Debug.LogWarning($"{name}: 달걀과 바구니 아이템 연결을 확인해 주세요.", this);
            return;
        }
        if (quickbar.SelectedItem != basket || inventory.GetQuantity(basket) < 1)
        {
            Debug.Log($"{name}: 바구니를 보유하고 선택해야 달걀을 수집할 수 있습니다.", this);
            return;
        }
        collecting = true;
        try
        {
            if (!inventory.AddItem(eggItem, 1))
            {
                Debug.Log($"{name}: 인벤토리 수량 한도로 수집할 수 없습니다.", this);
                return;
            }
            available = false;
            MissionProgress mission = interactor.GetComponent<MissionProgress>();
            if (mission != null) mission.RecordEggCollection(1);
            Debug.Log($"{name}: 달걀 1개 수집 완료. 보유 수량: {inventory.GetQuantity(eggItem)}", this);
            gameObject.SetActive(false);
        }
        finally { collecting = false; }
    }
}
