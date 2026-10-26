using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Function-check view for the shared player feedback; the team will replace the presentation later.
// Bottom centre: "[F] ..." for the target PlayerInteractor would actually pick (same FindTarget rule).
// Above the quickbar / interaction prompt: result notifications from PlayerFeedback.Notify, stacked so consecutive messages are not lost.
// PlayerFeedback.NotifyUntilClick lines use the same panel look, sit nearest the quickbar and never fade;
// while one is up a transparent full-screen blocker takes left clicks, so the dismissing click reaches no HUD button or Hex.
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
    private RectTransform untilClickPanel;
    private CanvasGroup untilClickGroup;
    private TMP_Text untilClickText;
    private GameObject clickBlocker;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private float untilClickAge;
    private bool waitingForRelease;

    private void OnEnable()
    {
        EnsureBuilt();
        PlayerFeedback.Notified += Show;
        PlayerFeedback.UntilClickChanged += RefreshUntilClick;
        RefreshUntilClick();
    }

    // Suspended scenes (regional travel) and disabled views drop what they were showing; nothing stale returns.
    // An undismissed NotifyUntilClick line stays queued in PlayerFeedback and shows again on the next enable.
    private void OnDisable()
    {
        PlayerFeedback.Notified -= Show;
        PlayerFeedback.UntilClickChanged -= RefreshUntilClick;
        foreach (Entry entry in entries) if (entry.rect != null) Destroy(entry.rect.gameObject);
        entries.Clear();
        SetPrompt(null);
        waitingForRelease = false;
        if (untilClickPanel != null) untilClickPanel.gameObject.SetActive(false);
        if (clickBlocker != null) clickBlocker.SetActive(false);
    }

    private void Update() => HandleUntilClickInput();

    private void LateUpdate()
    {
        RefreshPrompt();
        AgeNotifications(Time.unscaledDeltaTime);
        FadeInUntilClick(Time.unscaledDeltaTime);
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
        if (untilClickPanel != null && untilClickPanel.gameObject.activeSelf)
            offset = untilClickPanel.sizeDelta.y + EntrySpacing;
        // Keep the latest notification nearest the quickbar; older entries stack upward.
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            entries[i].rect.anchoredPosition = new Vector2(0f, offset);
            offset += entries[i].rect.sizeDelta.y + EntrySpacing;
        }
    }

    // ---- Click-to-dismiss line ----

    private void RefreshUntilClick()
    {
        if (untilClickPanel == null) return;
        string message = PlayerFeedback.CurrentUntilClick;
        bool show = message != null;
        if (show && (!untilClickPanel.gameObject.activeSelf || untilClickText.text != message))
        {
            untilClickText.text = message;
            float textHeight = untilClickText.GetPreferredValues(message, NotificationWidth - TextInsetX * 2f, Mathf.Infinity).y;
            untilClickPanel.sizeDelta = new Vector2(NotificationWidth, Mathf.Max(EntryHeight, Mathf.Ceil(textHeight) + TextInsetY * 2f));
            untilClickAge = 0f;
            untilClickGroup.alpha = 0f;
        }
        untilClickPanel.gameObject.SetActive(show);
        // The blocker outlives the line until the dismissing press is released (see HandleUntilClickInput).
        clickBlocker.SetActive(show || waitingForRelease);
        LayoutNotifications();
    }

    private void HandleUntilClickInput()
    {
        if (clickBlocker == null || !clickBlocker.activeSelf) return;
        Mouse mouse = Mouse.current;
        if (waitingForRelease)
        {
            // Hex placement / camera pan check the UI under the pointer on press and while held, so the
            // blocker stays until release; a click that started on it never turns into a drag or pick.
            if (mouse != null && mouse.leftButton.isPressed) return;
            waitingForRelease = false;
            RefreshUntilClick();
            return;
        }
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || !PointerOnBlocker(mouse.position.ReadValue())) return;
        waitingForRelease = true;
        PlayerFeedback.DismissUntilClick();
    }

    // Only a press that lands on the blocker dismisses: modal UI drawn above this canvas
    // (TradingPostUI, QuestWindow, PauseMenu) keeps its own clicks.
    private bool PointerOnBlocker(Vector2 screen)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return true;
        uiHits.Clear();
        events.RaycastAll(new PointerEventData(events) { position = screen }, uiHits);
        return uiHits.Count > 0 && uiHits[0].gameObject == clickBlocker;
    }

    private void FadeInUntilClick(float deltaTime)
    {
        if (untilClickPanel == null || !untilClickPanel.gameObject.activeSelf || untilClickGroup.alpha >= 1f) return;
        untilClickAge += deltaTime;
        untilClickGroup.alpha = Mathf.Clamp01(untilClickAge / FadeInDuration);
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
        // The raycaster only ever hits the click blocker below; every other graphic here has raycastTarget off,
        // so ordinary notifications never take clicks from the HUD or the build bar.
        root.AddComponent<GraphicRaycaster>();

        RectTransform blocker = CreateRect("UntilClickBlocker", root.transform);
        Stretch(blocker);
        Image blockerImage = blocker.gameObject.AddComponent<Image>();
        blockerImage.color = Color.clear;
        blockerImage.raycastTarget = true;
        clickBlocker = blocker.gameObject;
        clickBlocker.SetActive(false);

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

        // Same pill as a notification (sprite, 480 width, wrapped 19pt text), built once and reused.
        untilClickPanel = CreateRect("UntilClickNotification", notificationArea);
        untilClickPanel.anchorMin = untilClickPanel.anchorMax = untilClickPanel.pivot = new Vector2(0.5f, 0f);
        ApplySprite(untilClickPanel.gameObject.AddComponent<Image>(), notificationSprite);
        untilClickGroup = untilClickPanel.gameObject.AddComponent<CanvasGroup>();
        untilClickGroup.blocksRaycasts = untilClickGroup.interactable = false;
        untilClickText = CreateText("Text", untilClickPanel, "", 19f);
        Stretch((RectTransform)untilClickText.transform);
        untilClickText.rectTransform.offsetMin = new Vector2(TextInsetX, TextInsetY);
        untilClickText.rectTransform.offsetMax = new Vector2(-TextInsetX, -TextInsetY);
        untilClickText.textWrappingMode = TextWrappingModes.Normal;
        untilClickText.overflowMode = TextOverflowModes.Overflow;
        untilClickPanel.anchoredPosition = Vector2.zero;
        untilClickPanel.gameObject.SetActive(false);
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
