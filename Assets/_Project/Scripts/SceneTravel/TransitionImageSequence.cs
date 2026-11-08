using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Presentation only. SceneTransitionService owns scene readiness and input locking.
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class TransitionImageSequence : MonoBehaviour
{
    [SerializeField] private Sprite[] frames = new Sprite[0];
    [SerializeField, Min(0.01f)] private float frameDuration = 0.12f;

    private Image display;
    private Coroutine playback;
    public bool IsComplete { get; private set; } = true;

    public void Play(bool flipHorizontal = false)
    {
        Hide();
        if (frames == null || frames.Length == 0 || System.Array.Exists(frames, frame => frame == null))
        {
            Debug.LogError("Transition sequence has missing frames; keeping the black fade.", this);
            return;
        }
        EnsureDisplay();
        // Set explicitly on every play so a return trip cannot flip the next departure.
        // Mirror only the image around its centre; the black curtain stays unchanged.
        display.rectTransform.localScale = new Vector3(flipHorizontal ? -1f : 1f, 1f, 1f);
        display.enabled = true;
        IsComplete = false;
        playback = StartCoroutine(PlayFrames());
    }

    private IEnumerator PlayFrames()
    {
        // Do not derive an index from elapsed time: a slow load must never skip frames.
        foreach (Sprite frame in frames)
        {
            display.sprite = frame;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, frameDuration));
        }
        IsComplete = true;
        playback = null;
        // Keep the last sprite visible until the service explicitly calls Hide().
    }

    public void Hide()
    {
        if (playback != null) StopCoroutine(playback);
        playback = null;
        IsComplete = true;
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
        // The service's full-screen opaque black curtain fills any aspect-ratio bars
        // and transparent pixels, so the underlying scene cannot show through.
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
