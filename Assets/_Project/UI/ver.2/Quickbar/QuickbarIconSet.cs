using System;
using UnityEngine;

// UI-only presentation data. ItemDefinition and inventory quantities stay unchanged.
[CreateAssetMenu(menuName = "Ummate/UI/Quickbar Icon Set", fileName = "QuickbarIcons")]
public sealed class QuickbarIconSet : ScriptableObject
{
    [Serializable]
    private struct Entry
    {
        public ItemDefinition item;
        public Sprite sprite;
    }
    [SerializeField] private Entry[] entries;
    public Sprite GetIcon(ItemDefinition item)
    {
        if (item == null || entries == null) return null;
        foreach (Entry entry in entries)
            if (entry.item == item) return entry.sprite;
        return null;
    }
}
