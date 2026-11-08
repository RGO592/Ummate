using System;
using UnityEngine;

// Saved as an int: append new values only, never reorder.
public enum QuestStatus { Locked, Active, Completed }

// One quest's static data. Runtime progress lives in QuestLog, never in this asset.
// Values marked "임시" are prototype values that still need a design decision.
[CreateAssetMenu(menuName = "Susupjung/Quest", fileName = "NewQuest")]
public sealed class QuestDefinition : ScriptableObject
{
    public enum QuestCategory { Main, Side }

    [Serializable]
    public sealed class Objective
    {
        [Tooltip("Stable save key inside this quest. Do not rename after saves exist.")]
        public string id;
        public string description;
        [Tooltip("Gameplay event that advances this objective. None = condition not implemented yet (never advances).")]
        public QuestEventType eventType;
        [Tooltip("Optional filter: only this crop / flower / sold item counts. Empty = any.")]
        public ItemDefinition item;
        [Tooltip("Optional filter: e.g. the scene name for RegionEntered. Empty = any.")]
        public string target;
        [Tooltip("Events with one of these targets never count, e.g. a habitat an earlier quest already covers.")]
        public string[] excludedTargets = new string[0];
        [Min(1)] public int requiredAmount = 1;
        [TextArea, Tooltip("Shown in the Tip panel while this is the current objective.")]
        public string tip;
        [TextArea, Tooltip("Sequential quests only. Shown until clicked when this objective becomes the current one.")]
        public string startMessage;
        [Tooltip("QuestNavigationTarget id the player's arrow points at. Empty = no arrow.")]
        public string navigationTargetId;
        [Tooltip("Optional: the scene holding that target. From any other scene the arrow points at the exit leading there.")]
        public string navigationScene;

        // Whether an event (or a recorded habitat) with this target counts for the objective.
        public bool Accepts(string eventTarget) =>
            (string.IsNullOrEmpty(target) || target == eventTarget) &&
            (excludedTargets == null || eventTarget == null || Array.IndexOf(excludedTargets, eventTarget) < 0);
    }

    [Serializable]
    public sealed class ItemGrant
    {
        public ItemDefinition item;
        [Min(1)] public int quantity = 1;
    }

    [SerializeField, Tooltip("Stable save key (Q01, Q02 ...). Do not rename after saves exist.")]
    private string id;
    [SerializeField] private string title;
    [SerializeField, TextArea(2, 6)] private string description;
    [SerializeField] private QuestCategory category = QuestCategory.Main;
    [SerializeField, Tooltip("Every one must be Completed before this quest becomes Active.")]
    private QuestDefinition[] prerequisites = new QuestDefinition[0];
    [SerializeField, Tooltip("Completed when every objective is done. No objectives = never completes on its own.")]
    private Objective[] objectives = new Objective[0];
    [SerializeField, Tooltip("Objectives are shown and progressed one at a time, in data order. An event that fits a later objective before its turn does not count.")]
    private bool sequentialObjectives;
    [SerializeField, Tooltip("Objective IDs older saves may still hold. They are ignored on load instead of rejecting the save.")]
    private string[] retiredObjectiveIds = new string[0];
    [SerializeField, Tooltip("Transition quest: completes by itself on the frame after it becomes Active (or after a load that left it Active). No player action; its objectives are filled in.")]
    private bool completeOnStart;

    [Header("Start (granted once, when the quest becomes Active during play)")]
    [SerializeField] private ItemGrant[] startItems = new ItemGrant[0];
    [SerializeField, TextArea, Tooltip("Optional line shown when the quest starts, e.g. a spirit hint.")]
    private string startMessage;
    [SerializeField, Tooltip("Show the start message until the player left-clicks it, instead of as a timed notification.")]
    private bool startMessageUntilClick;

    [Header("Reward (granted once, on completion)")]
    [SerializeField] private ItemGrant[] rewardItems = new ItemGrant[0];
    [SerializeField, Min(0)] private int rewardGold;
    [SerializeField, Tooltip("Rewards shown in the quest window but not granted yet (no item/system exists).")]
    private string[] pendingRewards = new string[0];

    public string Id => id;
    public string Title => string.IsNullOrWhiteSpace(title) ? name : title;
    public string Description => description;
    public QuestCategory Category => category;
    public QuestDefinition[] Prerequisites => prerequisites ?? new QuestDefinition[0];
    public Objective[] Objectives => objectives ?? new Objective[0];
    public bool SequentialObjectives => sequentialObjectives;
    public string[] RetiredObjectiveIds => retiredObjectiveIds ?? new string[0];
    public bool CompleteOnStart => completeOnStart;
    public ItemGrant[] StartItems => startItems ?? new ItemGrant[0];
    public string StartMessage => startMessage;
    public bool StartMessageUntilClick => startMessageUntilClick;
    public ItemGrant[] RewardItems => rewardItems ?? new ItemGrant[0];
    public int RewardGold => Mathf.Max(0, rewardGold);
    public string[] PendingRewards => pendingRewards ?? new string[0];
}
