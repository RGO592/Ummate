using System;
using System.Collections.Generic;
using UnityEngine;

// Single entry point for selling and buying. Selling removes items first; gold is granted only after removal succeeded.
// Buying takes gold first; items are granted only after payment succeeded.
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

    // Today's market price (MarketPriceSystem: base Sell Price × today's multiplier); the base price when no market
    // is loaded. Quality is not implemented yet; a future multiplier also belongs here, so callers never change.
    // Only the price changes: TrySell below keeps its remove-items-then-pay transaction and rollback.
    public static int GetUnitSellPrice(ItemDefinition item) =>
        item != null && item.CanSell ? MarketPriceSystem.CurrentSellPriceOf(item) : 0;

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

    // ---- Buying (potions now; seeds, saplings, feed, restoration materials later use the same path) ----

    public enum BuyResult { Bought, InvalidRequest, NotForSale, NotEnoughGold, InventoryFull, Failed }

    public static int GetUnitBuyPrice(ItemDefinition item) => item != null && item.CanBuy ? item.BuyPrice : 0;

    // Gold is taken first; the item is granted only after payment succeeded. A failed grant refunds the gold.
    public static BuyResult TryBuy(PlayerInventory inventory, PlayerWallet wallet, ItemDefinition item,
        int quantity, out int spent)
    {
        spent = 0;
        if (inventory == null || wallet == null || item == null || quantity <= 0) return BuyResult.InvalidRequest;
        if (!item.CanBuy) return BuyResult.NotForSale;
        long total = (long)GetUnitBuyPrice(item) * quantity;
        if (total <= 0) return BuyResult.NotForSale;
        if (total > int.MaxValue || !wallet.HasGold((int)total)) return BuyResult.NotEnoughGold;
        if (inventory.GetQuantity(item) > int.MaxValue - quantity) return BuyResult.InventoryFull;

        // Wallet listeners (HUD refresh) run inside TrySpendGold; judge success by the committed balance.
        int goldBefore = wallet.Gold;
        bool paid;
        try { paid = wallet.TrySpendGold((int)total); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            paid = wallet.Gold == goldBefore - (int)total;
        }
        if (!paid) return BuyResult.Failed;

        int before = inventory.GetQuantity(item);
        bool granted;
        try { granted = inventory.AddItem(item, quantity); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            granted = inventory.GetQuantity(item) == before + quantity;
        }
        if (!granted)
        {
            // Never leave the player without both gold and items.
            wallet.AddGold((int)total);
            return BuyResult.Failed;
        }
        spent = (int)total;
        return BuyResult.Bought;
    }
}
