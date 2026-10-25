using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Function-check view for the shared player feedback; the team will replace the presentation later.
// Bottom centre: "[F] ..." for the target PlayerInteractor would actually pick (same FindTarget rule).
// Above the quickbar / interaction prompt: result notifications from PlayerFeedback.Notify, stacked so consecutive messages are not lost.
// Builds its own overlay canvas at runtime (same approach as TradingPostUI); one per scene that needs it.
[DisallowMultipleComponent]
public sealed class PlayerFeedbackUI : MonoBehaviour
{
    [Header("Appearance (UI ver.2 assets)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite promptSprite;
    [SerializeField] private Sprite notificationSprite;
    [SerializeField, Tooltip("Above the HUD, below modal UI such as TradingPostUI (200).")]
    private int sortingOrder = 100;

    [Header("Interaction prompt")]
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private string interactKeyLabel = "F";
    [SerializeField, Tooltip("Canvas units (1280×720) above the bottom edge. The quickbar ends at 100.")]
    private float promptBottomOffset = 112f;

    [Header("Notifications")]
    [SerializeField, Min(0.5f)] private float notificationDuration = 2f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.4f;
    [SerializeField, Min(1)] private int maxVisible = 4;
    [SerializeField, Tooltip("Canvas units (1280×720) above the bottom edge. Keeps the 112–156 interaction-prompt lane clear.")]
    private float notificationBottomOffset = 168f;

    private const float FadeInDuration = 0.15f;
    private const float EntryHeight = 44f;
    private const float NotificationWidth = 480f;
    private const float TextInsetX = 20f;
    private const float TextInsetY = 12f;
    private const float EntrySpacing = 6f;
    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color PanelFallback = new Color(0.98f, 0.95f, 0.88f);

    private sealed class Entry
    {
        public RectTransform rect;
        public CanvasGroup group;
        public string message;
        public float age;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private GameObject root;
    private RectTransform promptPanel;
    private TMP_Text promptText;
    private RectTransform notificationArea;
    private string shownPrompt;

    private void OnEnable()
    {
        EnsureBuilt();
        PlayerFeedback.Notified += Show;
    }

    // Suspended scenes (regional travel) and disabled views drop what they were showing; nothing stale returns.
    private void OnDisable()
    {
        PlayerFeedback.Notified -= Show;
        foreach (Entry entry in entries) if (entry.rect != null) Destroy(entry.rect.gameObject);
        entries.Clear();
        SetPrompt(null);
    }

    private void LateUpdate()
    {
        RefreshPrompt();
        AgeNotifications(Time.unscaledDeltaTime);
    }

    // ---- Interaction prompt ----

    private void RefreshPrompt()
    {
        string prompt = null;
        // Modal UI and sleep disable PlayerInteractor through their existing input lock lists, which hides this too.
        if (interactor != null && interactor.isActiveAndEnabled && !SceneTransitionService.IsTraveling)
        {
            Interactable target = interactor.FindTarget();
            if (target != null) prompt = target.GetInteractionPrompt(interactor.gameObject);
        }
        SetPrompt(prompt);
    }

    private void SetPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) prompt = null;
        if (prompt == shownPrompt || promptPanel == null) return;
        shownPrompt = prompt;
        promptPanel.gameObject.SetActive(prompt != null);
        if (prompt == null) return;
        promptText.text = $"<b>[{interactKeyLabel}]</b>  {prompt}";
        promptPanel.sizeDelta = new Vector2(promptText.GetPreferredValues(promptText.text).x + 48f, 44f);
    }

    // ---- Notifications ----

    private void Show(string message)
    {
        if (root == null) return;
        // The same message again (e.g. a repeated failure) restarts its timer instead of stacking.
        Entry newest = entries.Count > 0 ? entries[entries.Count - 1] : null;
        if (newest != null && newest.message == message)
        {
            newest.age = Mathf.Min(newest.age, FadeInDuration);
            return;
        }
        // Oldest first: every message is shown at least briefly before it is pushed out.
        while (entries.Count >= maxVisible) RemoveAt(0);

        RectTransform rect = CreateRect("Notification", notificationArea);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        ApplySprite(rect.gameObject.AddComponent<Image>(), notificationSprite);
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;
        TMP_Text text = CreateText("Text", rect, message, 19f);
        Stretch((RectTransform)text.transform);
        text.rectTransform.offsetMin = new Vector2(TextInsetX, TextInsetY);
        text.rectTransform.offsetMax = new Vector2(-TextInsetX, -TextInsetY);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        // Typical short feedback fits one or two lines at 19pt. Measure instead of clipping
        // unusually long notices: the same fixed width can grow vertically if necessary.
        float textHeight = text.GetPreferredValues(message, NotificationWidth - TextInsetX * 2f, Mathf.Infinity).y;
        rect.sizeDelta = new Vector2(NotificationWidth, Mathf.Max(EntryHeight, Mathf.Ceil(textHeight) + TextInsetY * 2f));
        entries.Add(new Entry { rect = rect, group = group, message = message });
        LayoutNotifications();
    }

    private void AgeNotifications(float deltaTime)
    {
        bool removed = false;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            entry.age += deltaTime;
            float fadeStart = notificationDuration;
            if (entry.age >= fadeStart + fadeDuration) { RemoveAt(i); removed = true; continue; }
            entry.group.alpha = entry.age < FadeInDuration ? entry.age / FadeInDuration
                : entry.age < fadeStart ? 1f : 1f - (entry.age - fadeStart) / fadeDuration;
        }
        if (removed) LayoutNotifications();
    }

    private void RemoveAt(int index)
    {
        if (entries[index].rect != null) Destroy(entries[index].rect.gameObject);
        entries.RemoveAt(index);
    }

    private void LayoutNotifications()
    {
        float offset = 0f;
        // Keep the latest notification nearest the quickbar; older entries stack upward.
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            entries[i].rect.anchoredPosition = new Vector2(0f, offset);
            offset += entries[i].rect.sizeDelta.y + EntrySpacing;
        }
    }

    // ---- Construction ----

    private void EnsureBuilt()
    {
        if (root != null) return;
        root = new GameObject("PlayerFeedbackCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        // Same reference as the existing HUD canvas, so offsets line up with the quickbar and Tip panel.
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: feedback never takes clicks from the HUD or the build bar.

        promptPanel = CreateRect("InteractionPrompt", root.transform);
        promptPanel.anchorMin = promptPanel.anchorMax = promptPanel.pivot = new Vector2(0.5f, 0f);
        promptPanel.anchoredPosition = new Vector2(0f, promptBottomOffset);
        ApplySprite(promptPanel.gameObject.AddComponent<Image>(), promptSprite);
        promptText = CreateText("Text", promptPanel, "", 20f);
        Stretch((RectTransform)promptText.transform);
        promptPanel.gameObject.SetActive(false);

        notificationArea = CreateRect("Notifications", root.transform);
        notificationArea.anchorMin = notificationArea.anchorMax = notificationArea.pivot = new Vector2(0.5f, 0f);
        notificationArea.anchoredPosition = new Vector2(0f, notificationBottomOffset);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void ApplySprite(Image image, Sprite sprite)
    {
        image.raycastTarget = false;
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : PanelFallback;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        // HUD panel sprites have ~24-28 px borders; halve them so 40-44 unit tall pills keep round corners.
        if (image.type == Image.Type.Sliced) image.pixelsPerUnitMultiplier = 2f;
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
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
}
