using System;
using UnityEngine;

// Single entry point for using an item from the quickbar. Today the only effect is energy recovery
// (ItemDefinition.EnergyRestore); another recovery item only needs that value set on its asset.
// The item is consumed only when the effect really applies: full energy keeps the item.
public static class ItemUseService
{
    public enum UseResult { Used, NotUsable, NotOwned, NoEffect, Failed }

    public static UseResult TryUse(PlayerInventory inventory, PlayerEnergy energy, ItemDefinition item)
    {
        if (inventory == null || item == null || !item.IsUsable) return UseResult.NotUsable;
        if (inventory.GetQuantity(item) < 1)
        {
            PlayerFeedback.Notify($"{item.DisplayName}이(가) 없어.");
            return UseResult.NotOwned;
        }
        if (energy == null)
        {
            Debug.LogWarning($"{item.DisplayName}: 기운(PlayerEnergy)을 찾을 수 없어 사용하지 않았습니다.");
            return UseResult.Failed;
        }
        if (energy.IsFull)
        {
            AudioManager.Play(GameSfx.ActionFail);
            PlayerFeedback.Notify("이미 기운이 가득해.");
            return UseResult.NoEffect;
        }

        // Inventory listeners (quickbar refresh) run inside TryConsume; judge success by the committed quantity.
        int before = inventory.GetQuantity(item);
        bool consumed;
        try { consumed = inventory.TryConsume(item, 1); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            consumed = inventory.GetQuantity(item) == before - 1;
        }
        if (!consumed) return UseResult.Failed;

        int restored = energy.Restore(item.EnergyRestore);
        Debug.Log($"{item.DisplayName} 사용: 기운 +{restored} ({energy.CurrentEnergy}/{energy.MaxEnergy})");
        PlayerFeedback.Notify($"기운이 {restored} 회복됐다.");
        return UseResult.Used;
    }
}
