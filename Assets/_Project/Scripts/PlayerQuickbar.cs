using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerQuickbar : MonoBehaviour
{
    public const int SlotCount = 5;
    [SerializeField] private ItemDefinition[] slots = new ItemDefinition[SlotCount];
    [SerializeField, Range(0, SlotCount - 1)] private int selectedSlotIndex;
    private PlayerInventory inventory;
    public event Action Changed;

    // Code uses zero-based indices; the displayed slot number is 1 through 5.
    public int SelectedSlotIndex => selectedSlotIndex;
    public int SelectedSlotNumber => selectedSlotIndex + 1;
    public ItemDefinition SelectedItem => GetSlotItem(selectedSlotIndex);
    public int SelectedQuantity => Inventory.GetQuantity(SelectedItem);
    private PlayerInventory Inventory => inventory != null ? inventory : (inventory = GetComponent<PlayerInventory>());

    private void OnValidate()
    {
        if (slots == null || slots.Length != SlotCount) Array.Resize(ref slots, SlotCount);
        selectedSlotIndex = Mathf.Clamp(selectedSlotIndex, 0, SlotCount - 1);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !Application.isFocused) return;
        if (keyboard.digit1Key.wasPressedThisFrame) SelectSlot(0);
        else if (keyboard.digit2Key.wasPressedThisFrame) SelectSlot(1);
        else if (keyboard.digit3Key.wasPressedThisFrame) SelectSlot(2);
        else if (keyboard.digit4Key.wasPressedThisFrame) SelectSlot(3);
        else if (keyboard.digit5Key.wasPressedThisFrame) SelectSlot(4);
    }

    public ItemDefinition GetSlotItem(int index)
    {
        return index >= 0 && index < SlotCount && slots != null && index < slots.Length ? slots[index] : null;
    }

    public bool AssignSlot(int index, ItemDefinition item)
    {
        if (index < 0 || index >= SlotCount) return false;
        if (slots == null || slots.Length != SlotCount) Array.Resize(ref slots, SlotCount);
        slots[index] = item;
        Changed?.Invoke();
        return true;
    }

    public bool SelectSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return false;
        selectedSlotIndex = index;
        Changed?.Invoke();
        return true;
    }

    public bool TryConsumeSelected(int amount = 1) => Inventory.TryConsume(SelectedItem, amount);
}
