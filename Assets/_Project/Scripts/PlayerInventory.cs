using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    [Serializable]
    private struct StartingItem
    {
        public ItemDefinition item;
        [Min(1)] public int quantity;
    }

    [SerializeField] private StartingItem[] startingItems = Array.Empty<StartingItem>();
    private readonly Dictionary<ItemDefinition, int> quantities = new Dictionary<ItemDefinition, int>();
    private bool initialized;
    public event Action Changed;

    private void Awake() => Initialize();

    public Dictionary<ItemDefinition, int> CaptureQuantities()
    {
        Initialize();
        return new Dictionary<ItemDefinition, int>(quantities);
    }

    public void RestoreQuantities(System.Collections.Generic.IDictionary<ItemDefinition, int> saved)
    {
        initialized = true;
        quantities.Clear();
        foreach (var entry in saved)
            if (entry.Value > 0) quantities.Add(entry.Key, entry.Value);
        Changed?.Invoke();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        foreach (StartingItem entry in startingItems)
        {
            if (entry.item == null || entry.quantity <= 0) continue;
            quantities.TryGetValue(entry.item, out int current);
            quantities[entry.item] = (int)Math.Min(int.MaxValue, (long)current + entry.quantity);
        }
    }

    public int GetQuantity(ItemDefinition item)
    {
        Initialize();
        return item != null && quantities.TryGetValue(item, out int count) ? count : 0;
    }

    public bool AddItem(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        int current = GetQuantity(item);
        if (amount > int.MaxValue - current) return false;
        quantities[item] = current + amount;
        Changed?.Invoke();
        return true;
    }

    // Atomic: insufficient quantity leaves the inventory unchanged.
    public bool TryConsume(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        int current = GetQuantity(item);
        if (current < amount) return false;
        if (current == amount) quantities.Remove(item);
        else quantities[item] = current - amount;
        Changed?.Invoke();
        return true;
    }
}
