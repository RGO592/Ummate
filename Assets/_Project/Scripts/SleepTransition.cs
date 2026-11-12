using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SleepTransition : MonoBehaviour
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private CanvasGroup overlay;
    [SerializeField] private TMP_Text morningLabel;
    [SerializeField] private Rigidbody playerBody;
    [SerializeField] private Behaviour[] inputComponents;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.3f;
    [SerializeField, Min(0f)] private float morningDuration = 0.45f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.35f;
    [Header("Temporary sleep illustration")]
    [SerializeField, Tooltip("Optional override; otherwise uses Resources/Transitions/Sleep.")]
    private Sprite sleepIllustration;
    [SerializeField, Min(0f)] private float sleepImageDuration = 0.8f;

    private bool[] previouslyEnabled;
    private UnityEngine.UI.Image sleepImage;
    private CanvasGroup morningPopup;
    public bool IsSleeping { get; private set; }
    public event System.Action SleepCompleted;

    private void Awake() => ClearOverlay();

    public bool TrySleep()
    {
        if (IsSleeping || !isActiveAndEnabled || gameDay == null || overlay == null || morningLabel == null)
            return false;
        IsSleeping = true;
        LockInput();
        StartCoroutine(Sequence());
        return true;
    }

    // Keep presentation here; GameDay remains responsible for advancing the day.
    private IEnumerator Sequence()
    {
        try
        {
            morningLabel.text = "";
            overlay.blocksRaycasts = true;
            EnsureSleepImage(); // Load before fading, while input is already locked.
            AudioManager.Play(GameSfx.Sleep); // Bedtime confirmed: as the fade starts.
            yield return FadeTo(1f, fadeOutDuration);
            if (sleepImage != null)
            {
                sleepImage.enabled = true;
                // Present the illustration before daily processing can take up a frame.
                yield return null;
            }
            gameDay.Sleep();
            if (sleepImage != null)
            {
                yield return new WaitForSecondsRealtime(sleepImageDuration);
                sleepImage.enabled = false;
            }
            EnsureMorningPopup();
            morningPopup.alpha = 1f;
            morningLabel.text = $"{gameDay.CurrentDay}일차 아침";
            AudioManager.Play(GameSfx.Morning); // With the morning text.
            yield return FadeTo(0f, fadeInDuration);
            yield return new WaitForSecondsRealtime(morningDuration);
            yield return HideMorningPopup();
        }
        finally { Finish(); }
        // Only a normally completed sequence saves; cancellation/OnDisable must not save.
        SleepCompleted?.Invoke();
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        float from = overlay.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            overlay.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        overlay.alpha = target;
    }

    private void EnsureMorningPopup()
    {
        if (morningPopup != null) return;
        var panel = new GameObject("MorningPopup", typeof(RectTransform),
            typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
        panel.transform.SetParent(overlay.transform.parent, false);
        panel.transform.SetAsLastSibling();
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(300f, 70f);
        rect.anchoredPosition = new Vector2(0f, -55f);
        var background = panel.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.96f, 0.91f, 0.79f, 0.98f);
        background.raycastTarget = false;
        morningPopup = panel.GetComponent<CanvasGroup>();
        morningPopup.blocksRaycasts = false;
        morningPopup.interactable = false;
        morningLabel.transform.SetParent(panel.transform, false);
        var labelRect = morningLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 8f);
        labelRect.offsetMax = new Vector2(-16f, -8f);
        morningLabel.enableAutoSizing = false;
        morningLabel.fontSize = 30f;
        morningLabel.color = new Color(0.31f, 0.25f, 0.18f);
        morningLabel.alignment = TextAlignmentOptions.Center;
        morningLabel.raycastTarget = false;
    }

    private IEnumerator HideMorningPopup()
    {
        const float duration = 0.25f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            morningPopup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        morningPopup.alpha = 0f;
    }

    private void EnsureSleepImage()
    {
        if (sleepImage != null) return;
        if (sleepIllustration == null) sleepIllustration = Resources.Load<Sprite>("Transitions/Sleep");
        if (sleepIllustration == null) return; // Keep the existing sleep flow if the optional art is unavailable.
        var imageObject = new GameObject("SleepIllustration", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        imageObject.transform.SetParent(overlay.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        sleepImage = imageObject.GetComponent<UnityEngine.UI.Image>();
        sleepImage.sprite = sleepIllustration;
        sleepImage.preserveAspect = true;
        sleepImage.raycastTarget = true;
        sleepImage.enabled = false;
        // The existing opaque overlay fills letterbox bars and transparent pixels.
    }

    private void LockInput()
    {
        int count = inputComponents != null ? inputComponents.Length : 0;
        previouslyEnabled = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Behaviour input = inputComponents[i];
            if (input == null || input == this) continue;
            previouslyEnabled[i] = input.enabled;
            input.enabled = false;
        }
        if (playerBody != null)
        {
            Vector3 velocity = playerBody.linearVelocity;
            playerBody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }
    }

    private void Finish()
    {
        ClearOverlay();
        if (previouslyEnabled != null)
        {
            for (int i = 0; i < previouslyEnabled.Length; i++)
                if (inputComponents[i] != null && inputComponents[i] != this)
                    inputComponents[i].enabled = previouslyEnabled[i];
            previouslyEnabled = null;
        }
        IsSleeping = false;
    }

    private void ClearOverlay()
    {
        if (morningPopup != null) morningPopup.alpha = 0f;
        if (sleepImage != null) sleepImage.enabled = false;
        if (overlay != null) { overlay.alpha = 0f; overlay.blocksRaycasts = false; }
        if (morningLabel != null) morningLabel.text = "";
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish();
    }
}
