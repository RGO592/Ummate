using System;
using UnityEngine;

// Saved in quest assets as an int: append new values only, never reorder.
public enum QuestEventType
{
    None,
    CropPlanted, CropWatered, CropHarvested,
    FlowerPlanted, FlowerWatered, FlowerHarvested,
    TreeChopped,
    ItemSold,
    RegionEntered
}

// Gameplay systems report what actually succeeded; QuestLog decides what it means.
// Quests never drive FarmPlot / FlowerBed / trees / trade directly.
public static class QuestEvents
{
    // type, item (crop / flower / wood / sold item, may be null), target (e.g. scene name, may be null)
    public static event Action<QuestEventType, ItemDefinition, string> Raised;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners() => Raised = null;

    public static void Raise(QuestEventType type, ItemDefinition item = null, string target = null)
    {
        if (type == QuestEventType.None || Raised == null) return;
        // Quest bookkeeping must never break the gameplay action that reported it.
        try { Raised.Invoke(type, item, target); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
}
