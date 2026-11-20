using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation only. SceneTransitionService owns scene readiness and input locking.
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class TransitionImageSequence : MonoBehaviour
{
    [SerializeField] private Sprite[] frames = new Sprite[0];
    [SerializeField, Min(0.01f)] private float frameDuration = 0.12f;
    [Header("Region travel loading screen")]
    [SerializeField] private bool showLoadingOverlay;
    [SerializeField, Min(0f)] private float minimumDisplayTime = 2f;
    [SerializeField] private TMP_FontAsset loadingFont;
    [SerializeField] private string[] tips;

    private Image display;
    private Coroutine playback;
    private TMP_Text tipLabel;
    private TMP_Text loadingLabel;
    private GameObject loadingOverlay;
    private int nextTip;
    private int loadingStep;
    private double shownAt;
    private static readonly string[] LoadingSteps = { "로딩.", "로딩..", "로딩..." };
    // The travel service also waits for the destination and its spawn point to be ready.
    public bool IsComplete => playback == null && (loadingOverlay == null || !loadingOverlay.activeSelf ||
        Time.realtimeSinceStartupAsDouble - shownAt >= minimumDisplayTime);

    public void Play(bool flipHorizontal = false)
    {
        Hide();
        EnsureDisplay();
        if (showLoadingOverlay) ShowLoadingOverlay();
        if (frames == null || frames.Length == 0 || System.Array.Exists(frames, frame => frame == null))
        {
            Debug.LogError("Transition sequence has missing frames; keeping the black fade.", this);
            return;
        }
        // Mirror only the image around its centre; the labels and curtain stay unchanged.
        display.rectTransform.localScale = new Vector3(flipHorizontal ? -1f : 1f, 1f, 1f);
        display.enabled = true;
        playback = StartCoroutine(PlayFrames());
    }

    private void ShowLoadingOverlay()
    {
        if (loadingOverlay == null)
        {
            loadingOverlay = new GameObject("LoadingOverlay", typeof(RectTransform));
            loadingOverlay.transform.SetParent(transform, false);
            Stretch((RectTransform)loadingOverlay.transform);
            tipLabel = CreateLabel("Tip", new Vector2(0.12f, 0.5f), new Vector2(0.88f, 0.5f),
                new Vector2(0f, 70f), Vector2.zero, 28f);
            loadingLabel = CreateLabel("LoadingIndicator", Vector2.right, Vector2.right,
                new Vector2(100f, 42f), new Vector2(-72f, 42f), 21f);
        }
        bool hasTip = tips != null && tips.Length > 0;
        tipLabel.transform.parent.gameObject.SetActive(hasTip);
        if (hasTip)
        {
            tipLabel.text = tips[nextTip % tips.Length];
            nextTip = (nextTip + 1) % tips.Length;
        }
        shownAt = Time.realtimeSinceStartupAsDouble;
        loadingStep = 0;
        loadingLabel.text = LoadingSteps[0];
        loadingOverlay.SetActive(true);
    }

    private IEnumerator PlayFrames()
    {
        // Do not derive an index from elapsed time: a slow load must never skip frames.
        foreach (Sprite frame in frames)
        {
            display.sprite = frame;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, frameDuration));
        }
        playback = null;
        // Keep the last sprite visible until the service explicitly calls Hide().
    }

    public void Hide()
    {
        if (playback != null) StopCoroutine(playback);
        playback = null;
        if (loadingOverlay != null) loadingOverlay.SetActive(false);
        if (display != null) display.enabled = false;
    }

    private void EnsureDisplay()
    {
        if (display != null) return;
        Stretch((RectTransform)transform);
        GameObject child = new GameObject("SequenceFrame", typeof(RectTransform), typeof(Image));
        child.transform.SetParent(transform, false);
        Stretch((RectTransform)child.transform);
        display = child.GetComponent<Image>();
        display.preserveAspect = true;
        display.raycastTarget = true;
        display.enabled = false;
        // The service's full-screen opaque black curtain fills any aspect-ratio bars
        // and transparent pixels, so the underlying scene cannot show through.
    }

    private TMP_Text CreateLabel(string labelName, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 size, Vector2 position, float fontSize)
    {
        var panel = new GameObject(labelName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(loadingOverlay.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var backdrop = panel.GetComponent<Image>();
        backdrop.color = new Color(0.09f, 0.07f, 0.05f, 0.8f);
        backdrop.raycastTarget = false;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(rect, false);
        var label = textObject.GetComponent<TextMeshProUGUI>();
        Stretch(label.rectTransform);
        if (loadingFont != null) label.font = loadingFont;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14f;
        label.fontSizeMax = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.margin = new Vector4(8f, 3f, 8f, 3f);
        label.color = new Color(1f, 0.96f, 0.86f);
        label.raycastTarget = false;
        return label;
    }

    private void Update()
    {
        if (loadingOverlay == null || !loadingOverlay.activeSelf) return;
        int step = (int)((Time.realtimeSinceStartupAsDouble - shownAt) / 0.35) % LoadingSteps.Length;
        if (step == loadingStep) return;
        loadingStep = step;
        loadingLabel.text = LoadingSteps[step];
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnDisable() => Hide();
}
