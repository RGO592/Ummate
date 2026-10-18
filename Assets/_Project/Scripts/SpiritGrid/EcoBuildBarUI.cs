using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bottom build bar: one slot per tile kind (select to place), remove / cancel for a picked tile,
// and a single status line. Reads placement + grid state; only calls the placement controller's public API.
[DisallowMultipleComponent]
public sealed class EcoBuildBarUI : MonoBehaviour
{
    [Serializable]
    public struct Slot
    {
        public EnvironmentTileType type;
        public Button button;
        public Image background;
        [Tooltip("Colour swatch now; assign an icon sprite later and it is shown instead.")]
        public Image icon;
        public Sprite iconSprite;
        public TMP_Text nameLabel;
        public TMP_Text costLabel;
    }

    [SerializeField] private EcoPlacementController placement;
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private Slot[] slots = new Slot[0];
    [SerializeField] private Sprite normalSlotSprite;
    [SerializeField] private Sprite selectedSlotSprite;
    [SerializeField, Range(1f, 1.15f)] private float selectedScale = 1.08f;
    [SerializeField] private Button removeButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text statusLabel;

    private void Awake()
    {
        foreach (Slot slot in slots)
        {
            if (slot.button == null) continue;
            EnvironmentTileType type = slot.type;
            slot.button.onClick.AddListener(() => { if (placement != null) placement.SelectType(type); });
            if (slot.nameLabel != null) slot.nameLabel.text = EnvironmentTileTypes.DisplayName(type);
            if (slot.icon != null)
            {
                bool hasSprite = slot.iconSprite != null;
                slot.icon.sprite = slot.iconSprite;
                slot.icon.color = hasSprite ? Color.white : EnvironmentTileTypes.DefaultColor(type);
                slot.icon.preserveAspect = hasSprite;
            }
        }
        if (removeButton != null) removeButton.onClick.AddListener(() => { if (placement != null) placement.RemovePicked(); });
        if (cancelButton != null) cancelButton.onClick.AddListener(() => { if (placement != null) placement.CancelPick(); });
    }

    private void Update()
    {
        if (placement == null || grid == null) return;
        bool picking = placement.HasPicked;
        foreach (Slot slot in slots)
        {
            bool selected = !picking && slot.type == placement.SelectedType;
            if (slot.background != null)
            {
                Sprite sprite = selected && selectedSlotSprite != null ? selectedSlotSprite : normalSlotSprite;
                if (sprite != null && slot.background.sprite != sprite) slot.background.sprite = sprite;
                slot.background.transform.localScale = Vector3.one * (selected ? selectedScale : 1f);
            }
            if (slot.costLabel != null)
            {
                string cost = EcoDevMode.FreePlacement ? "DEV 무료" : $"{grid.GetCost(slot.type)} G";
                if (slot.costLabel.text != cost) slot.costLabel.text = cost;
            }
        }
        if (removeButton != null) removeButton.interactable = picking;
        if (cancelButton != null) cancelButton.interactable = picking;
        if (statusLabel != null)
        {
            string status = BuildStatus(picking);
            if (statusLabel.text != status) statusLabel.text = status;
        }
    }

    private string BuildStatus(bool picking)
    {
        if (picking)
        {
            string name = EnvironmentTileTypes.DisplayName(placement.PickedType);
            string hover = placement.HasHover && placement.HoverCoord != placement.PickedCoord ? $" · {placement.HoverStatus}" : "";
            return $"{name} 이동 중 — 빈 칸을 클릭해 옮기거나 [제거]{hover}";
        }
        if (placement.HasHover) return $"{EnvironmentTileTypes.DisplayName(placement.SelectedType)} · {placement.HoverStatus}";
        return string.IsNullOrEmpty(placement.LastMessage)
            ? $"{EnvironmentTileTypes.DisplayName(placement.SelectedType)} 배치 중 — 빈 칸을 클릭하세요"
            : placement.LastMessage;
    }
}
