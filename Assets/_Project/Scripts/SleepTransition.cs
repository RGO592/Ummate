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

    private bool[] previouslyEnabled;
    public bool IsSleeping { get; private set; }

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
            yield return FadeTo(1f, fadeOutDuration);
            gameDay.Sleep();
            morningLabel.text = $"{gameDay.CurrentDay}일차 아침";
            yield return new WaitForSecondsRealtime(morningDuration);
            yield return FadeTo(0f, fadeInDuration);
        }
        finally { Finish(); }
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
        if (overlay != null) { overlay.alpha = 0f; overlay.blocksRaycasts = false; }
        if (morningLabel != null) morningLabel.text = "";
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish();
    }
}
