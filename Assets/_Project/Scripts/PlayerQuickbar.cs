using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerQuickbar : MonoBehaviour
{
    public const int SlotCount = 10;
    [SerializeField] private ItemDefinition[] slots = new ItemDefinition[SlotCount];
    [SerializeField, Range(0, SlotCount - 1)] private int selectedSlotIndex;
    private PlayerInventory inventory;
    private float nextScrollTime;
    public event Action Changed;

    // Code uses zero-based indices; key 0 selects the tenth slot.
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
        if (!Application.isFocused) return;
        Keyboard keyboard = Keyboard.current;
        int numberSlot = -1;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) numberSlot = 0;
            else if (keyboard.digit2Key.wasPressedThisFrame) numberSlot = 1;
            else if (keyboard.digit3Key.wasPressedThisFrame) numberSlot = 2;
            else if (keyboard.digit4Key.wasPressedThisFrame) numberSlot = 3;
            else if (keyboard.digit5Key.wasPressedThisFrame) numberSlot = 4;
            else if (keyboard.digit6Key.wasPressedThisFrame) numberSlot = 5;
            else if (keyboard.digit7Key.wasPressedThisFrame) numberSlot = 6;
            else if (keyboard.digit8Key.wasPressedThisFrame) numberSlot = 7;
            else if (keyboard.digit9Key.wasPressedThisFrame) numberSlot = 8;
            else if (keyboard.digit0Key.wasPressedThisFrame) numberSlot = 9;
        }
        // Number keys take priority if both inputs arrive in the same frame.
        if (numberSlot >= 0) SelectSlot(numberSlot);
        else if (Mouse.current != null) HandleScroll(Mouse.current.scroll.ReadValue().y);
    }
    private void HandleScroll(float scrollY)
    {
        if (Mathf.Abs(scrollY) < 0.01f || Time.unscaledTime < nextScrollTime) return;
        // Wheel deltas vary by device (often 120). Use only the direction,
        // with a short burst guard: one accepted input always advances one slot.
        nextScrollTime = Time.unscaledTime + 0.10f;
        int step = scrollY < 0 ? 1 : -1;
        SelectSlot((selectedSlotIndex + step + SlotCount) % SlotCount);
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

