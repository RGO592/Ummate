using UnityEngine;

[CreateAssetMenu(menuName = "Ummate/Item", fileName = "NewItem")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}
