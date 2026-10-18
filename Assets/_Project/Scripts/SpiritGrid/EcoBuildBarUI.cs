using System;
using TMPro;
using UnityEngine;

// Bottom build bar: drag a slot to place; remove / cancel affect the current drag,
// and a single status line. Reads placement + grid state; only calls the placement controller's public API.
[DisallowMultipleComponent]
public sealed class EcoBuildBarUI : MonoBehaviour
{
    [Serializable]
    public struct Slot
    {
        public EnvironmentTileType type;
        public UnityEngine.UI.Button button;
        public UnityEngine.UI.Image background;
        [Tooltip("Colour swatch now; assign an icon sprite later and it is shown instead.")]
        public UnityEngine.UI.Image icon;
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
    [SerializeField, Tooltip("철거: removes the picked tile, otherwise toggles remove mode.")]
    private UnityEngine.UI.Button removeButton;
    [SerializeField] private Color removeActiveColor = new Color(1f, 0.45f, 0.3f);
    [SerializeField] private UnityEngine.UI.Button cancelButton;
    [SerializeField] private TMP_Text statusLabel;

    private TMP_Text removeLabel;
    private UnityEngine.UI.Image removeImage;
    private Color removeIdleColor = Color.white;

    private void Awake()
    {
        foreach (Slot slot in slots)
        {
            if (slot.button == null) continue;
            EnvironmentTileType type = slot.type;
            EcoBuildSlotDrag dragSource = slot.button.GetComponent<EcoBuildSlotDrag>();
            if (dragSource == null) dragSource = slot.button.gameObject.AddComponent<EcoBuildSlotDrag>();
            dragSource.Initialize(placement, type);
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
        if (removeButton != null)
        {
            removeLabel = removeButton.GetComponentInChildren<TMP_Text>(true);
            removeImage = removeButton.targetGraphic as UnityEngine.UI.Image;
            if (removeImage != null) removeIdleColor = removeImage.color;
            removeButton.onClick.AddListener(() =>
            {
                if (placement == null) return;
                if (placement.HasPicked) placement.RemovePicked();
                else placement.ToggleRemoveMode();
            });
        }
        if (cancelButton != null) cancelButton.onClick.AddListener(() =>
        {
            if (placement == null) return;
            placement.CancelPick();
            placement.SetRemoveMode(false);
        });
    }

    private void Update()
    {
        if (placement == null || grid == null) return;
        bool picking = placement.HasPicked;
        bool removing = placement.RemoveMode;
        foreach (Slot slot in slots)
        {
            bool selected = !picking && !removing && slot.type == placement.SelectedType;
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
        if (removeLabel != null)
        {
            string text = picking ? "선택 제거" : removing ? "철거 중 (X)" : "철거 (X)";
            if (removeLabel.text != text) removeLabel.text = text;
        }
        if (removeImage != null) removeImage.color = removing ? removeActiveColor : removeIdleColor;
        if (cancelButton != null) cancelButton.interactable = placement.IsDragging || removing;
        if (statusLabel != null)
        {
            string status = BuildStatus(picking);
            if (statusLabel.text != status) statusLabel.text = status;
        }
    }

    private string BuildStatus(bool picking)
    {
        if (placement.RemoveMode)
            return placement.HasHover ? $"철거 모드 · {placement.HoverStatus}" : "철거 모드 — 클릭하거나 드래그해서 제거 · 우클릭/Esc/X로 종료";
        if (placement.IsDragging)
        {
            string name = EnvironmentTileTypes.DisplayName(picking ? placement.PickedType : placement.SelectedType);
            return $"{name} {(picking ? "이동" : "배치")} · {placement.HoverStatus} · 우클릭/Esc 취소";
        }
        if (placement.HasHover) return placement.HoverStatus;
        return string.IsNullOrEmpty(placement.LastMessage)
            ? "하단 환경 슬롯을 원하는 칸으로 드래그하세요 · 배치된 환경물도 드래그해서 이동"
            : placement.LastMessage;
    }
}
