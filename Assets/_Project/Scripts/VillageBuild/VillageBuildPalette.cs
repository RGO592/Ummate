using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Button = UnityEngine.UI.Button;

// Temporary bottom build palette, separate from the Quickbar. Builds its own overlay canvas at runtime
// (same approach as TradingPostUI / QuestWindow); the final look replaces this view, not the controller.
[DisallowMultipleComponent]
public sealed class VillageBuildPalette : MonoBehaviour
{
    [Header("Appearance (UI ver.2 assets)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite panelSprite;
    [SerializeField] private Sprite buttonSprite;
    [SerializeField, Tooltip("Below PlayerFeedbackUI (100) and QuestWindow (250).")]
    private int sortingOrder = 90;

    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color SelectedColor = new Color(1f, 0.9f, 0.62f);
    private static readonly Color LockedColor = new Color(0.6f, 0.58f, 0.55f);
    private const string IdleHint = "시설을 고르거나, 설치한 시설을 클릭해 옮기세요.   WASD·휠클릭 드래그: 시점 이동   휠: 확대   B / ESC: 종료";

    private readonly List<(VillageFacilityDefinition facility, Button button, Image image)> entries =
        new List<(VillageFacilityDefinition, Button, Image)>();
    private GameObject root;
    private TMP_Text hint;

    public event Action<VillageFacilityDefinition> FacilityPicked;
    public event Action ExitRequested;

    public void Show(IReadOnlyList<VillageFacilityDefinition> facilities, Func<VillageFacilityDefinition, bool> isUnlocked)
    {
        EnsureBuilt(facilities);
        foreach (var entry in entries) entry.button.interactable = isUnlocked(entry.facility);
        SetSelected(null);
        root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    public void SetSelected(VillageFacilityDefinition selected)
    {
        foreach (var entry in entries)
            entry.image.color = entry.facility == selected ? SelectedColor : Color.white;
        SetHint(selected != null ? $"{selected.DisplayName}: 좌클릭 설치   R: 회전   우클릭 / ESC: 취소" : null);
    }

    // Null restores the idle guidance.
    public void SetHint(string message)
    {
        if (hint != null) hint.text = string.IsNullOrEmpty(message) ? IdleHint : message;
    }

    private void EnsureBuilt(IReadOnlyList<VillageFacilityDefinition> facilities)
    {
        if (root != null) return;
        root = new GameObject("VillageBuildPaletteCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreateRect("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        // Above the quickbar, which ends at 150 in these canvas units.
        panel.anchoredPosition = new Vector2(0f, 160f);
        panel.sizeDelta = new Vector2(1160f, 170f);
        Image background = panel.gameObject.AddComponent<Image>();
        ApplySprite(background, panelSprite, new Color(0.98f, 0.95f, 0.88f));

        hint = CreateText("Hint", panel, "", 21f);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 1f);
        hintRect.anchorMax = Vector2.one;
        hintRect.pivot = new Vector2(0.5f, 1f);
        hintRect.anchoredPosition = new Vector2(0f, -12f);
        hintRect.sizeDelta = new Vector2(-40f, 36f);

        RectTransform row = CreateRect("Buttons", panel);
        row.anchorMin = Vector2.zero;
        row.anchorMax = Vector2.one;
        row.offsetMin = new Vector2(24f, 18f);
        row.offsetMax = new Vector2(-24f, -52f);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        foreach (VillageFacilityDefinition facility in facilities)
        {
            if (facility == null) continue;
            VillageFacilityDefinition picked = facility;
            Vector2Int size = facility.Footprint;
            Button button = CreateButton(facility.Id, row, $"{facility.DisplayName}\n<size=70%>{size.x}×{size.y}</size>",
                () => FacilityPicked?.Invoke(picked));
            entries.Add((facility, button, button.GetComponent<Image>()));
        }
        CreateButton("Exit", row, "건설 종료", () => ExitRequested?.Invoke());
        root.SetActive(false);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallback)
    {
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : fallback;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string text, float size)
    {
        TextMeshProUGUI label = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = TextColor;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    private Button CreateButton(string objectName, Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        ApplySprite(image, buttonSprite, new Color(0.9f, 0.84f, 0.72f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // WASD pans the camera, not UI focus.
        ColorBlock colors = button.colors;
        colors.disabledColor = LockedColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);
        TMP_Text label = CreateText("Label", rect, text, 26f);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return button;
    }
}
