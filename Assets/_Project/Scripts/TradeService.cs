using System;
using System.Collections.Generic;
using UnityEngine;

// Single entry point for selling. Items are removed first; gold is granted only after removal succeeded.
public static class TradeService
{
    public enum SellResult { Sold, InvalidRequest, NotSellable, NotEnoughItems, WalletFull, Failed, Blocked }

    // Optional vetoes (e.g. QuestSeedSaleGuard keeping the trial's last wheat seed). Each returns a
    // player-facing reason to refuse the sale, or null to allow it. Checked before anything changes.
    private static readonly List<Func<PlayerInventory, ItemDefinition, int, string>> saleBlockers =
        new List<Func<PlayerInventory, ItemDefinition, int, string>>();
    public static string LastBlockReason { get; private set; } = "";

    public static void AddSaleBlocker(Func<PlayerInventory, ItemDefinition, int, string> blocker)
    {
        if (blocker != null && !saleBlockers.Contains(blocker)) saleBlockers.Add(blocker);
    }

    public static void RemoveSaleBlocker(Func<PlayerInventory, ItemDefinition, int, string> blocker) => saleBlockers.Remove(blocker);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        saleBlockers.Clear();
        LastBlockReason = "";
    }

    // Quality is not implemented yet. A future version can return
    // BaseSellPrice × QualityMultiplier here without changing callers.
    public static int GetUnitSellPrice(ItemDefinition item) => item != null && item.CanSell ? item.BaseSellPrice : 0;

    public static SellResult TrySell(PlayerInventory inventory, PlayerWallet wallet, ItemDefinition item,
        int quantity, out int earned)
    {
        earned = 0;
        if (inventory == null || wallet == null || item == null || quantity <= 0) return SellResult.InvalidRequest;
        if (!item.CanSell) return SellResult.NotSellable;
        int before = inventory.GetQuantity(item);
        if (before < quantity) return SellResult.NotEnoughItems;
        long total = (long)GetUnitSellPrice(item) * quantity;
        if (total <= 0) return SellResult.NotSellable;
        if (total > int.MaxValue - (long)wallet.Gold) return SellResult.WalletFull;
        foreach (Func<PlayerInventory, ItemDefinition, int, string> blocker in saleBlockers.ToArray())
        {
            string reason;
            try { reason = blocker(inventory, item, quantity); }
            catch (Exception exception) { Debug.LogException(exception); reason = null; }
            if (string.IsNullOrEmpty(reason)) continue;
            LastBlockReason = reason;
            return SellResult.Blocked;
        }

        // Inventory listeners (UI refresh) run inside TryConsume; judge success by the committed quantity.
        bool removed;
        try { removed = inventory.TryConsume(item, quantity); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            removed = inventory.GetQuantity(item) == before - quantity;
        }
        if (!removed) return SellResult.Failed;

        int goldBefore = wallet.Gold;
        bool paid;
        try { paid = wallet.AddGold((int)total); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            paid = wallet.Gold == goldBefore + (int)total;
        }
        if (!paid)
        {
            // Never leave the player without both items and gold.
            inventory.AddItem(item, quantity);
            return SellResult.Failed;
        }
        earned = (int)total;
        QuestEvents.Raise(QuestEventType.ItemSold, item);
        return SellResult.Sold;
    }
}
