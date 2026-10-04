using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Temporary test controls only: removing this component removes T/Y test shortcuts.
public sealed class InventoryTestUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerQuickbar quickbar;
    [SerializeField] private TMP_Text[] slotLabels;
    [SerializeField] private TMP_Text statusLabel;
    private string lastAction = "준비";

    private void OnEnable()
    {
        if (inventory != null) inventory.Changed += Refresh;
        if (quickbar != null) quickbar.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= Refresh;
        if (quickbar != null) quickbar.Changed -= Refresh;
    }

    private void LateUpdate()
    {
        Keyboard keyboard = Keyboard.current;
        if (inventory == null || quickbar == null || keyboard == null || !Application.isFocused) return;
        if (keyboard.tKey.wasPressedThisFrame)
        {
            lastAction = quickbar.TryConsumeSelected() ? "1개 차감 (테스트용)" : "차감 불가: 빈 슬롯 또는 재고 없음";
            Refresh();
        }
        else if (keyboard.yKey.wasPressedThisFrame)
        {
            lastAction = inventory.AddItem(quickbar.SelectedItem) ? "1개 추가 (테스트용)" : "추가 불가: 빈 슬롯 또는 수량 한도";
            Refresh();
        }
    }

    private void Refresh()
    {
        if (inventory == null || quickbar == null) return;
        for (int i = 0; i < PlayerQuickbar.SlotCount; i++)
        {
            if (slotLabels == null || i >= slotLabels.Length || slotLabels[i] == null) continue;
            ItemDefinition item = quickbar.GetSlotItem(i);
            bool selected = i == quickbar.SelectedSlotIndex;
            int keyNumber = (i + 1) % 10;
            slotLabels[i].text = $"{(selected ? "> " : "")}[{keyNumber}]\n{(item != null ? item.DisplayName : "빈 슬롯")}\n수량: {inventory.GetQuantity(item)}";
            slotLabels[i].color = selected ? Color.yellow : Color.white;
        }
        if (statusLabel != null)
            statusLabel.text = $"선택됨: {quickbar.SelectedSlotNumber} | {lastAction}\n1~9, 0: 선택    T: 1개 차감    Y: 1개 추가    (테스트용 조작)";
    }
}
