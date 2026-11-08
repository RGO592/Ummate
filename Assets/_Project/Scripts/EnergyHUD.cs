using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Function-check Energy HUD (bar + "73 / 100"), bottom-right. The team will replace the presentation later.
// Builds its own overlay canvas at runtime (same approach as PlayerFeedbackUI / TradingPostUI); one per scene.
// Event driven: redraws only on PlayerEnergy.Changed (spend, sleep, potion, load) — never polled per frame.
// In region scenes it shows the suspended village player's energy (PlayerEnergy.Active).
[DisallowMultipleComponent]
public sealed class EnergyHUD : MonoBehaviour
{
    [Header("Appearance (임시 — 최종 스타일은 이후 교체)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField, Tooltip("Optional panel background. Empty uses a plain translucent panel.")]
    private Sprite panelSprite;
    [SerializeField, Tooltip("Below PlayerFeedbackUI (100) and modal UI such as TradingPostUI (200).")]
    private int sortingOrder = 90;
    [SerializeField, Tooltip("Canvas units (1280×720) from the bottom-right corner. The village quickbar ends at x 986, y 100.")]
    private Vector2 cornerOffset = new Vector2(20f, 20f);
    [SerializeField] private Vector2 size = new Vector2(250f, 46f);
    [SerializeField] private string title = "기운";
    [SerializeField] private Color panelColor = new Color(0.98f, 0.95f, 0.88f, 0.92f);
    [SerializeField] private Color textColor = new Color(0.27f, 0.19f, 0.12f);
    [SerializeField] private Color barBackColor = new Color(0.35f, 0.28f, 0.2f, 0.35f);
    [SerializeField] private Color fillColor = new Color(0.35f, 0.75f, 0.3f);
    [SerializeField] private Color lowFillColor = new Color(0.95f, 0.6f, 0.15f);
    [SerializeField] private Color emptyTextColor = new Color(0.8f, 0.15f, 0.1f);
    [SerializeField, Range(0f, 1f), Tooltip("At or below this ratio the bar uses the low colour.")]
    private float lowRatio = 0.25f;

    private PlayerEnergy observed;
    private GameObject root;
    private RectTransform fill;
    private Image fillImage;
    private TMP_Text valueText;

    private void OnEnable()
    {
        EnsureBuilt();
        SceneManager.sceneLoaded += OnSceneLoaded;
        PlayerEnergy.Registered += Bind;
        Bind();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        PlayerEnergy.Registered -= Bind;
        Unbind();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Bind();

    // Test keys (Editor / development builds only), alongside the existing F8/F9 test keys: F6 = energy 0, F7 = full.
    // Lives here because the HUD is active in every gameplay scene; the village player is inactive during SpiritGrid visits.
    private void Update()
    {
        if (!(Application.isEditor || Debug.isDebugBuild) || !Application.isFocused || observed == null) return;
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null || SceneTransitionService.IsTraveling) return;
        if (keyboard.f6Key.wasPressedThisFrame)
        {
            observed.DebugSetEnergy(0);
            PlayerFeedback.Notify("테스트: 기운 0");
        }
        else if (keyboard.f7Key.wasPressedThisFrame)
        {
            observed.DebugSetEnergy(observed.MaxEnergy);
            PlayerFeedback.Notify("테스트: 기운 가득");
        }
    }

    // Rebound on every enable, so returning from another region shows the latest value at once.
    private void Bind()
    {
        PlayerEnergy next = PlayerEnergy.Active;
        if (next == observed && observed != null) { Refresh(); return; }
        Unbind();
        observed = next;
        if (observed != null) observed.Changed += Refresh;
        Refresh();
    }

    private void Unbind()
    {
        if (observed != null) observed.Changed -= Refresh;
        observed = null;
    }

    private void Refresh()
    {
        if (root == null) return;
        bool show = observed != null;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;
        int current = observed.CurrentEnergy, max = observed.MaxEnergy;
        float ratio = Mathf.Clamp01(current / (float)max);
        fill.anchorMax = new Vector2(ratio, 1f);
        fillImage.color = ratio <= lowRatio ? lowFillColor : fillColor;
        valueText.text = $"{current} / {max}";
        valueText.color = current <= 0 ? emptyTextColor : textColor;
    }

    private void EnsureBuilt()
    {
        if (root != null) return;
        root = new GameObject("EnergyHUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        // No GraphicRaycaster: the HUD never takes clicks (Hex / build clicks pass through).
        RectTransform panel = CreateRect("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-cornerOffset.x, cornerOffset.y);
        panel.sizeDelta = size;
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.raycastTarget = false;
        panelImage.sprite = panelSprite;
        panelImage.color = panelSprite != null ? Color.white : panelColor;
        if (panelSprite != null && panelSprite.border != Vector4.zero) panelImage.type = Image.Type.Sliced;

        TMP_Text titleText = CreateText("Title", panel, title, 18f, TextAlignmentOptions.MidlineLeft);
        RectTransform titleRect = (RectTransform)titleText.transform;
        titleRect.anchorMin = new Vector2(0f, 0f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 0.5f);
        titleRect.anchoredPosition = new Vector2(12f, 0f);
        titleRect.sizeDelta = new Vector2(48f, 0f);
        titleText.fontStyle = FontStyles.Bold;

        RectTransform bar = CreateRect("Bar", panel);
        bar.anchorMin = new Vector2(0f, 0.5f);
        bar.anchorMax = new Vector2(1f, 0.5f);
        bar.offsetMin = new Vector2(64f, -11f);
        bar.offsetMax = new Vector2(-12f, 11f);
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.color = barBackColor;
        barImage.raycastTarget = false;

        // Anchor-based fill: works without a sprite (Image.Type.Filled needs one).
        fill = CreateRect("Fill", bar);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;

        valueText = CreateText("Value", bar, "", 17f, TextAlignmentOptions.Center);
        RectTransform valueRect = (RectTransform)valueText.transform;
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = valueRect.offsetMax = Vector2.zero;
        valueText.fontStyle = FontStyles.Bold;

        root.SetActive(false);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string text, float fontSize, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI label = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = textColor;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }
}
