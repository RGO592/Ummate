using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class InventoryTestUI : MonoBehaviour
{
    [Serializable]
    private sealed class SlotView
    {
        public RectTransform visual;
        public Image background;
        public TMP_Text keyText;
        public TMP_Text quantityText;
        public Image icon;
        public Sprite itemIcon;
    }

    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerQuickbar quickbar;
    [SerializeField] private TMP_Text[] slotLabels;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private bool showDebugUI = false;
    [Header("Quickbar Appearance")]
    [SerializeField] private SlotView[] slotViews;
    [SerializeField] private QuickbarIconSet iconSet;
    [SerializeField] private Sprite normalSlotSprite;
    [SerializeField] private Sprite selectedSlotSprite;
    [SerializeField] private Image barBackground;
    [SerializeField] private Sprite barSprite;
    [SerializeField] private Image statusBackground;
    [SerializeField] private Sprite panelSprite;
    [Header("Selection Animation (Canvas units)")]
    [SerializeField, Range(1.05f, 1.08f)] private float selectedScale = 1.06f;
    [SerializeField, Range(0.08f, 0.15f)] private float animationDuration = 0.12f;
    [SerializeField, Min(0)] private float selectedLift = 2f;
    [SerializeField, Min(0)] private float neighborShift = 1.5f;
    [SerializeField, Min(0)] private float edgeInset = 2f;
    private readonly Vector2[] startPositions = new Vector2[PlayerQuickbar.SlotCount];
    private readonly Vector3[] startScales = new Vector3[PlayerQuickbar.SlotCount];
    private int displayedSelection = -1;
    private float elapsed;
    private string lastAction = "준비";

    private void OnEnable()
    {
        displayedSelection = -1;
        if (inventory != null) inventory.Changed += Refresh;
        if (quickbar != null) quickbar.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= Refresh;
        if (quickbar != null) quickbar.Changed -= Refresh;
    }

    private Vector2 TargetPosition(int index)
    {
        if (index == displayedSelection)
            return new Vector2(index == 0 ? edgeInset : index == PlayerQuickbar.SlotCount - 1 ? -edgeInset : 0, selectedLift);
        return new Vector2(index == displayedSelection - 1 ? -neighborShift : index == displayedSelection + 1 ? neighborShift : 0, 0);
    }

    private void AnimateSelection()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / Mathf.Max(0.001f, animationDuration)));
        for (int i = 0; slotViews != null && i < Mathf.Min(slotViews.Length, PlayerQuickbar.SlotCount); i++)
        {
            RectTransform visual = slotViews[i]?.visual;
            if (visual == null) continue;
            visual.anchoredPosition = Vector2.LerpUnclamped(startPositions[i], TargetPosition(i), t);
            visual.localScale = Vector3.LerpUnclamped(startScales[i], Vector3.one * (i == displayedSelection ? selectedScale : 1), t);
        }
    }

    private void LateUpdate()
    {
        if (statusBackground != null && statusBackground.gameObject.activeSelf != showDebugUI)
            statusBackground.gameObject.SetActive(showDebugUI);
        if (displayedSelection >= 0) AnimateSelection();
        Keyboard keyboard = Keyboard.current;
        if (inventory == null || quickbar == null || keyboard == null || !Application.isFocused) return;
        // Existing development shortcuts do not affect starting inventory.
        if (keyboard.tKey.wasPressedThisFrame)
        {
            lastAction = quickbar.TryConsumeSelected() ? "1개 차감 (테스트용)" : "차감 불가: 빈 슬롯 또는 재고 없음";
            Refresh();
        }
        else if (keyboard.yKey.wasPressedThisFrame)
        {
            lastAction = inventory.AddItem(quickbar.SelectedItem) ? "1개 추가 (테스트용)" : "추가 불가: 빈 슬롯 또는 수량 한도";
            Refresh();
        }
    }

    private void Refresh()
    {
        if (inventory == null || quickbar == null) return;
        bool first = displayedSelection < 0;
        bool selectionChanged = displayedSelection != quickbar.SelectedSlotIndex;
        displayedSelection = quickbar.SelectedSlotIndex;
        if (barBackground != null) barBackground.sprite = barSprite;
        if (statusBackground != null) statusBackground.sprite = panelSprite;
        for (int i = 0; i < PlayerQuickbar.SlotCount; i++)
        {
            ItemDefinition item = quickbar.GetSlotItem(i);
            bool selected = i == displayedSelection;
            SlotView view = slotViews != null && i < slotViews.Length ? slotViews[i] : null;
            if (slotLabels != null && i < slotLabels.Length && slotLabels[i] != null)
                slotLabels[i].text = view != null ? (item != null ? item.DisplayName : "빈 슬롯")
                    : $"[{(i + 1) % 10}]\n{(item != null ? item.DisplayName : "빈 슬롯")}\n{inventory.GetQuantity(item)}";
            if (view == null) continue;
            Sprite itemSprite = iconSet != null ? iconSet.GetIcon(item) : view.itemIcon;
            if (slotLabels != null && i < slotLabels.Length && slotLabels[i] != null)
                slotLabels[i].enabled = item == null || itemSprite == null;
            if (view.background != null) view.background.sprite = selected ? selectedSlotSprite : normalSlotSprite;
            if (view.keyText != null) view.keyText.text = ((i + 1) % 10).ToString();
            if (view.quantityText != null) view.quantityText.text = inventory.GetQuantity(item).ToString();
            if (view.icon != null)
            {
                view.icon.sprite = itemSprite;
                view.icon.enabled = item != null && itemSprite != null;
            }
            if (!selectionChanged || view.visual == null) continue;
            // Capture the current pose so rapid key changes do not snap or restart from neutral.
            startPositions[i] = first ? TargetPosition(i) : view.visual.anchoredPosition;
            startScales[i] = first ? Vector3.one * (selected ? selectedScale : 1) : view.visual.localScale;
            if (first)
            {
                view.visual.anchoredPosition = startPositions[i];
                view.visual.localScale = startScales[i];
            }
        }
        if (selectionChanged) elapsed = first ? animationDuration : 0;
        if (statusLabel != null)
            statusLabel.text = $"선택됨: {quickbar.SelectedSlotNumber} | {lastAction}\n1~9, 0: 선택    T: 1개 차감    Y: 1개 추가    (테스트용 조작)";
    }
}

