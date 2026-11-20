using System.Collections;
using System.Collections.Generic;
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
    private bool[] previouslyEnabled;
    private TransitionImageSequence sleepSequence;
    private CanvasGroup morningPopup;
    private readonly List<HudMask> hudMasks = new List<HudMask>();
    private readonly List<GameObject> visibleArrows = new List<GameObject>();
    public bool IsSleeping { get; private set; }
    public event System.Action SleepCompleted;

    private void Awake() => ClearOverlay();

    public bool TrySleep()
    {
        if (IsSleeping || !isActiveAndEnabled || gameDay == null || overlay == null || morningLabel == null)
            return false;
        IsSleeping = true;
        StartCoroutine(Sequence());
        return true;
    }

    // Keep presentation here; GameDay remains responsible for advancing the day.
    private IEnumerator Sequence()
    {
        try
        {
            HideHud();
            LockInput();
            morningLabel.text = "";
            overlay.blocksRaycasts = true;
            EnsureSleepSequence();
            AudioManager.Play(GameSfx.Sleep); // Bedtime confirmed: as the fade starts.
            yield return FadeTo(1f, fadeOutDuration);
            if (sleepSequence != null)
            {
                sleepSequence.Play();
                while (!sleepSequence.IsComplete) yield return null;
            }
            gameDay.Sleep();
            if (sleepSequence != null) sleepSequence.Hide();
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

    private void EnsureSleepSequence()
    {
        if (sleepSequence != null) return;
        var prefab = Resources.Load<GameObject>("Transitions/SleepSequence");
        if (prefab != null && prefab.TryGetComponent(out TransitionImageSequence sequence))
            sleepSequence = Instantiate(sequence, overlay.transform, false);
        else
            Debug.LogError("Missing sleep animation: Resources/Transitions/SleepSequence.", this);
    }

    // Keep each view's activeSelf and event subscriptions intact while the day changes.
    // This also masks canvases that a day-change listener may activate during sleep.
    private readonly struct HudMask
    {
        private readonly CanvasGroup group;
        private readonly float alpha;
        private readonly bool activeSelf, enabled, interactable, blocksRaycasts;

        public HudMask(GameObject view)
        {
            activeSelf = view.activeSelf;
            if (!view.TryGetComponent(out CanvasGroup existing)) existing = view.AddComponent<CanvasGroup>();
            group = existing;
            alpha = group.alpha;
            enabled = group.enabled;
            interactable = group.interactable;
            blocksRaycasts = group.blocksRaycasts;
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;
            group.enabled = true;
        }

        public void Restore()
        {
            if (group == null) return;
            if (group.gameObject.activeSelf != activeSelf) group.gameObject.SetActive(activeSelf);
            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = blocksRaycasts;
            group.enabled = enabled;
        }
    }

    private void HideHud()
    {
        // SleepOverlay shares InventoryTestCanvas with the quickbar, settings and Tip buttons.
        Transform sharedHud = overlay.transform.parent;
        foreach (Transform child in sharedHud)
            if (child != overlay.transform && (morningPopup == null || child != morningPopup.transform))
                hudMasks.Add(new HudMask(child.gameObject));

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.gameObject.scene != gameObject.scene || sharedHud.IsChildOf(canvas.transform) ||
                canvas.transform.IsChildOf(sharedHud)) continue;
            hudMasks.Add(new HudMask(canvas.gameObject));
        }
        // The navigation arrow is a world mesh, outside all UI canvases.
        foreach (var arrow in FindObjectsByType<QuestNavigationArrow>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (arrow.gameObject.scene != gameObject.scene) continue;
            visibleArrows.Add(arrow.gameObject);
            arrow.gameObject.SetActive(false);
        }
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
        // Enabling an input component can refresh its view in OnEnable.
        // Restore each view after those callbacks, before another frame is rendered.
        foreach (HudMask mask in hudMasks) mask.Restore();
        hudMasks.Clear();
        foreach (GameObject arrow in visibleArrows) if (arrow != null) arrow.SetActive(true);
        visibleArrows.Clear();
        IsSleeping = false;
    }

    private void ClearOverlay()
    {
        if (morningPopup != null) morningPopup.alpha = 0f;
        if (sleepSequence != null) sleepSequence.Hide();
        if (overlay != null) { overlay.alpha = 0f; overlay.blocksRaycasts = false; }
        if (morningLabel != null) morningLabel.text = "";
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish();
    }
}
