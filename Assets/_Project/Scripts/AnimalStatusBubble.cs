using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)] // Position after the camera has followed the player.
public sealed class AnimalStatusBubble : MonoBehaviour
{
    public enum Mood { Hungry, Happy, Full, Dislike }
    [Serializable] public sealed class MoodView
    {
        public Mood mood;
        public string text;
        public Sprite sprite;
    }
    [SerializeField] private MonoBehaviour feedSource;
    [SerializeField] private Transform animal;
    [SerializeField] private Renderer[] animalRenderers;
    [SerializeField, Tooltip("Optional head anchor. Without one, renderer bounds are used.")] private Transform positionAnchor;
    [SerializeField] private Transform player;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private GameDay gameDay;
    [SerializeField] private Canvas bubbleCanvas;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private UnityEngine.UI.Image statusIcon;
    [SerializeField] private UnityEngine.UI.Image statusBackground;
    [SerializeField, Tooltip("Hide the separate background when the assigned icon already includes its own frame.")]
    private bool iconsIncludeBackground;
    [SerializeField, Min(0)] private float heightOffset = 0.35f;
    [SerializeField, Min(0)] private float displayDistance = 9f;
    [SerializeField, Min(0)] private float reactionDuration = 1.5f;
    [SerializeField] private MoodView[] moods = {
        new MoodView { mood = Mood.Hungry, text = "배고픔" },
        new MoodView { mood = Mood.Happy, text = "행복함" },
        new MoodView { mood = Mood.Full, text = "배부름" },
        new MoodView { mood = Mood.Dislike, text = "싫어함" }
    };
    private IAnimalFeedStatus source;
    private Mood reaction;
    private float reactionUntil;
    private Mood? displayedMood;
    public Mood CurrentMood { get; private set; }
    public bool IsVisible => bubbleCanvas != null && bubbleCanvas.enabled;

    private void OnEnable()
    {
        source = feedSource as IAnimalFeedStatus;
        if (source != null) source.FeedReacted += React;
        if (gameDay != null) { gameDay.DayChanged += DayChanged; gameDay.StateRestored += ClearReaction; }
        ClearReaction();
        if (bubbleCanvas != null) bubbleCanvas.enabled = false;
    }
    private void OnDisable()
    {
        if (source != null) source.FeedReacted -= React;
        if (gameDay != null) { gameDay.DayChanged -= DayChanged; gameDay.StateRestored -= ClearReaction; }
        if (bubbleCanvas != null) bubbleCanvas.enabled = false;
    }
    private void React(FeedReaction value)
    {
        reaction = value == FeedReaction.Fed ? Mood.Happy : Mood.Dislike;
        reactionUntil = Time.unscaledTime + reactionDuration;
    }
    private void DayChanged(int previous, int current) => ClearReaction();
    private void ClearReaction() { reactionUntil = 0; displayedMood = null; }

    private void LateUpdate()
    {
        if (bubbleCanvas == null) return;
        bool valid = source != null && feedSource != null && feedSource.isActiveAndEnabled && animal != null;
        if (!valid) { bubbleCanvas.enabled = false; return; }
        CurrentMood = Time.unscaledTime < reactionUntil ? reaction : source.FedToday ? Mood.Full : Mood.Hungry;
        if (displayedMood != CurrentMood)
        {
            displayedMood = CurrentMood;
            MoodView entry = Array.Find(moods, m => m != null && m.mood == CurrentMood);
            bool useIcon = entry != null && entry.sprite != null;
            if (statusIcon != null) { statusIcon.sprite = entry?.sprite; statusIcon.gameObject.SetActive(useIcon); }
            if (statusBackground != null) statusBackground.enabled = !useIcon || !iconsIncludeBackground;
            if (statusText != null) { statusText.text = entry?.text ?? CurrentMood.ToString(); statusText.gameObject.SetActive(!useIcon); }
        }
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || player == null || (player.position - animal.position).sqrMagnitude > displayDistance * displayDistance)
        { bubbleCanvas.enabled = false; return; }
        Vector3 anchor;
        if (positionAnchor != null)
        {
            anchor = positionAnchor.position;
        }
        else
        {
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var renderer in animalRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) { bubbleCanvas.enabled = false; return; }
            anchor = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        // Both anchors and the bounds fallback use the same small vertical clearance.
        transform.position = anchor + Vector3.up * heightOffset;
        transform.rotation = viewCamera.transform.rotation;
        bubbleCanvas.enabled = Vector3.Dot(viewCamera.transform.forward, transform.position - viewCamera.transform.position) > 0;
    }
}
