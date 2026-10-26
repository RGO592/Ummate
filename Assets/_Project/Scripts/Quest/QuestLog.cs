using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runtime quest progress for the whole game. Lives on the FarmScene player next to PlayerInventory.
// FarmScene stays loaded (only suspended) while a region is visited, so this subscribes in Awake, not
// OnEnable, and keeps counting events raised there (arriving in SpiritGridScene completes Q05).
// Any number of quests can be Active at once; the "current main quest" is just the first Active Main one.
// Progress only comes from QuestEvents (real gameplay successes) and travel arrivals; loading copies
// the saved values and never grants items, rewards or progress.
[DisallowMultipleComponent]
public sealed class QuestLog : MonoBehaviour
{
    public sealed class Entry
    {
        internal Entry(QuestDefinition definition)
        {
            Definition = definition;
            Progress = new int[definition.Objectives.Length];
        }

        public QuestDefinition Definition { get; }
        public QuestStatus Status { get; internal set; }
        internal int[] Progress { get; }
        public int ObjectiveCount => Progress.Length;
        public int GetProgress(int objective) => Progress[objective];
        public int GetRequired(int objective) => Mathf.Max(1, Definition.Objectives[objective].requiredAmount);
        public bool IsObjectiveDone(int objective) => Progress[objective] >= GetRequired(objective);
        // A quest without objectives (condition not implemented yet) never completes on its own.
        public bool AllObjectivesDone => Progress.Length > 0 && Enumerable.Range(0, Progress.Length).All(IsObjectiveDone);
        // First unfinished objective in data order, or -1.
        public int CurrentObjective
        {
            get
            {
                for (int i = 0; i < Progress.Length; i++) if (!IsObjectiveDone(i)) return i;
                return -1;
            }
        }
    }

    [SerializeField, Tooltip("Every quest of the game in display order (Q01 ... Q10).")]
    private QuestDefinition[] quests = new QuestDefinition[0];
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerWallet wallet;
    [Header("HUD (the former mission panel label)")]
    [SerializeField] private TMP_Text hudLabel;
    [SerializeField] private string noQuestText = "진행 중인 퀘스트가 없습니다.";
    [SerializeField] private string noQuestTip = "J 키로 퀘스트 목록을 볼 수 있습니다.";

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Queue<(QuestEventType type, ItemDefinition item, string target)> pending =
        new Queue<(QuestEventType, ItemDefinition, string)>();
    private bool built, processing, subscribed;

    // Raised after any state or progress change (gameplay, load, new game).
    public event Action Changed;

    public IReadOnlyList<Entry> Entries { get { EnsureBuilt(); return entries; } }

    public Entry CurrentMain
    {
        get
        {
            EnsureBuilt();
            return entries.FirstOrDefault(e => e.Status == QuestStatus.Active && e.Definition.Category == QuestDefinition.QuestCategory.Main)
                ?? entries.FirstOrDefault(e => e.Status == QuestStatus.Active);
        }
    }

    // What the Tip panel shows: the current main quest's first unfinished objective.
    public string CurrentTip
    {
        get
        {
            Entry main = CurrentMain;
            if (main == null) return noQuestTip;
            int index = main.CurrentObjective;
            if (index < 0) return main.Definition.Description;
            QuestDefinition.Objective objective = main.Definition.Objectives[index];
            return string.IsNullOrWhiteSpace(objective.tip) ? objective.description : objective.tip;
        }
    }

    public bool SaveReferencesValid
    {
        get
        {
            if (inventory == null || wallet == null || quests == null || quests.Length == 0 || quests.Any(q => q == null)) return false;
            string[] ids = quests.Select(q => q.Id).ToArray();
            if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length) return false;
            foreach (QuestDefinition quest in quests)
            {
                string[] objectiveIds = quest.Objectives.Select(o => o != null ? o.id : null).ToArray();
                if (objectiveIds.Any(string.IsNullOrWhiteSpace) || objectiveIds.Distinct().Count() != objectiveIds.Length) return false;
                if (quest.Prerequisites.Any(p => p == null || !quests.Contains(p))) return false;
            }
            return true;
        }
    }

    public QuestStatus GetStatus(string id)
    {
        Entry entry = Find(id);
        return entry != null ? entry.Status : QuestStatus.Locked;
    }

    private void Awake()
    {
        EnsureBuilt();
        ResetToInitial();
        // FarmScene loads fresh for New Game and Load; a story line left from the previous run must not carry over.
        PlayerFeedback.ClearUntilClick();
        if (!SaveReferencesValid) Debug.LogWarning("QuestLog: 퀘스트 목록(ID·목표 ID·선행 퀘스트)과 인벤토리/지갑 연결을 확인해 주세요.", this);
        QuestEvents.Raised += OnQuestEvent;
        SceneTransitionService.Arrived += OnArrived;
        subscribed = true;
    }

    private void Start() => RefreshHud();

    private void OnDestroy()
    {
        if (!subscribed) return;
        QuestEvents.Raised -= OnQuestEvent;
        SceneTransitionService.Arrived -= OnArrived;
    }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;
        entries.Clear();
        if (quests == null) return;
        foreach (QuestDefinition quest in quests)
            if (quest != null && Find(quest.Id) == null) entries.Add(new Entry(quest));
    }

    private Entry Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Entry entry in entries) if (entry.Definition.Id == id) return entry;
        return null;
    }

    // Quests without prerequisites start Active. Their start items are never granted here: the new-game
    // inventory (GameSaveSystem.NewGame / PlayerInventory starting items) already supplies the first quest.
    private void ResetToInitial()
    {
        pending.Clear();
        foreach (Entry entry in entries)
        {
            entry.Status = QuestStatus.Locked;
            Array.Clear(entry.Progress, 0, entry.Progress.Length);
        }
        ActivateEligible(grant: false, announce: false);
    }

    // ---- Gameplay progress ----

    private void OnArrived(Scene scene) => QuestEvents.Raise(QuestEventType.RegionEntered, null, scene.name);

    private void OnQuestEvent(QuestEventType type, ItemDefinition item, string target)
    {
        // Rewards change the inventory; listeners reacting to that must not re-enter the same pass.
        pending.Enqueue((type, item, target));
        if (processing) return;
        processing = true;
        try
        {
            while (pending.Count > 0)
            {
                var next = pending.Dequeue();
                Apply(next.type, next.item, next.target);
            }
        }
        finally { processing = false; }
    }

    private void Apply(QuestEventType type, ItemDefinition item, string target)
    {
        bool changed = false, completed = false;
        foreach (Entry entry in entries)
        {
            if (entry.Status != QuestStatus.Active) continue;
            QuestDefinition.Objective[] objectives = entry.Definition.Objectives;
            for (int i = 0; i < objectives.Length; i++)
            {
                QuestDefinition.Objective objective = objectives[i];
                if (objective == null || objective.eventType != type || entry.IsObjectiveDone(i)) continue;
                if (objective.item != null && objective.item != item) continue;
                if (!string.IsNullOrEmpty(objective.target) && objective.target != target) continue;
                entry.Progress[i] = Mathf.Min(entry.GetRequired(i), entry.Progress[i] + 1);
                changed = true;
                if (entry.IsObjectiveDone(i))
                    PlayerFeedback.Notify($"퀘스트 목표 완료: {objective.description} {entry.Progress[i]} / {entry.GetRequired(i)}");
            }
            if (entry.AllObjectivesDone)
            {
                Complete(entry);
                completed = true;
            }
        }
        // Quests unlocked by this event start only now, so the same event never counts for them.
        if (completed) ActivateEligible(grant: true, announce: true);
        if (changed) NotifyChanged();
    }

    private void Complete(Entry entry)
    {
        QuestDefinition quest = entry.Definition;
        entry.Status = QuestStatus.Completed;
        GrantItems(quest.RewardItems, quest);
        if (quest.RewardGold > 0 && !wallet.AddGold(quest.RewardGold))
            Debug.LogWarning($"{quest.Id} 골드 보상을 지급하지 못했습니다(골드 한도).", this);
        Debug.Log($"퀘스트 완료: {quest.Id} {quest.Title}", this);
        PlayerFeedback.Notify($"퀘스트 완료: {quest.Title}");
    }

    private void ActivateEligible(bool grant, bool announce)
    {
        foreach (Entry entry in entries)
        {
            if (entry.Status != QuestStatus.Locked) continue;
            if (!entry.Definition.Prerequisites.All(p => p != null && GetStatus(p.Id) == QuestStatus.Completed)) continue;
            entry.Status = QuestStatus.Active;
            Array.Clear(entry.Progress, 0, entry.Progress.Length);
            if (announce)
            {
                Debug.Log($"새 퀘스트: {entry.Definition.Id} {entry.Definition.Title}", this);
                PlayerFeedback.Notify($"새 퀘스트: {entry.Definition.Title}");
                if (entry.Definition.StartMessageUntilClick) PlayerFeedback.NotifyUntilClick(entry.Definition.StartMessage);
                else PlayerFeedback.Notify(entry.Definition.StartMessage);
            }
            if (grant) GrantItems(entry.Definition.StartItems, entry.Definition);
        }
    }

    private void GrantItems(QuestDefinition.ItemGrant[] grants, QuestDefinition quest)
    {
        foreach (QuestDefinition.ItemGrant grant in grants)
        {
            if (grant == null || grant.item == null || grant.quantity <= 0) continue;
            if (inventory != null && inventory.AddItem(grant.item, grant.quantity))
                PlayerFeedback.Notify($"{grant.item.DisplayName} {grant.quantity}개를 받았습니다.");
            else Debug.LogWarning($"{quest.Id}: {grant.item.DisplayName} {grant.quantity}개를 지급하지 못했습니다.", this);
        }
    }

    private void NotifyChanged()
    {
        RefreshHud();
        try { Changed?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    private void RefreshHud()
    {
        if (hudLabel == null) return;
        Entry main = CurrentMain;
        if (main == null)
        {
            hudLabel.text = noQuestText;
            return;
        }
        int index = main.CurrentObjective;
        string line = index >= 0
            ? $"{main.Definition.Objectives[index].description} {main.GetProgress(index)} / {main.GetRequired(index)}"
            : "목표 준비 중";
        hudLabel.text = $"<b>{main.Definition.Title}</b>\n<size=85%>{line}</size>";
    }

    // ---- Save / load ----

    public GameSaveData.QuestSaveState[] CaptureState()
    {
        EnsureBuilt();
        return entries.Select(entry => new GameSaveData.QuestSaveState
        {
            id = entry.Definition.Id,
            state = entry.Status,
            objectives = entry.Definition.Objectives.Select((objective, i) =>
                new GameSaveData.ObjectiveSaveState { id = objective.id, progress = entry.Progress[i] }).ToArray()
        }).ToArray();
    }

    // Checked before any gameplay state changes. Unknown quest / objective IDs are rejected like unknown
    // item IDs; quests missing from the save (added later) are allowed and load as Locked.
    public bool TryValidateState(GameSaveData.QuestSaveState[] saved, out string error)
    {
        EnsureBuilt();
        error = null;
        if (saved == null || saved.Length == 0) return true;
        if (saved.Any(s => s == null || string.IsNullOrWhiteSpace(s.id)) || saved.Select(s => s.id).Distinct().Count() != saved.Length)
            return Fail("퀘스트 ID 누락/중복", out error);
        var byId = saved.ToDictionary(s => s.id);
        foreach (GameSaveData.QuestSaveState state in saved)
        {
            Entry entry = Find(state.id);
            if (entry == null) return Fail($"저장 파일의 퀘스트({state.id})가 현재 퀘스트 목록에 없습니다.", out error);
            if (!Enum.IsDefined(typeof(QuestStatus), state.state)) return Fail("퀘스트 상태 오류", out error);
            var objectives = state.objectives ?? new GameSaveData.ObjectiveSaveState[0];
            if (objectives.Any(o => o == null || string.IsNullOrWhiteSpace(o.id)) || objectives.Select(o => o.id).Distinct().Count() != objectives.Length)
                return Fail("퀘스트 목표 ID 누락/중복", out error);
            foreach (GameSaveData.ObjectiveSaveState objective in objectives)
            {
                int index = Array.FindIndex(entry.Definition.Objectives, o => o.id == objective.id);
                if (index < 0) return Fail($"저장 파일의 퀘스트 목표({state.id}/{objective.id})가 현재 데이터에 없습니다.", out error);
                if (objective.progress < 0 || objective.progress > entry.GetRequired(index)) return Fail("퀘스트 진행도 오류", out error);
            }
            if (state.state != QuestStatus.Locked && entry.Definition.Prerequisites.Any(p =>
                    !byId.TryGetValue(p.Id, out GameSaveData.QuestSaveState before) || before.state != QuestStatus.Completed))
                return Fail("퀘스트 진행 순서 오류", out error);
        }
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    // Load / new game only: states and counts are copied as saved. No start items, rewards or notices.
    public void RestoreState(GameSaveData.QuestSaveState[] saved)
    {
        EnsureBuilt();
        if (saved == null || saved.Length == 0)
        {
            ResetToInitial();
            NotifyChanged();
            return;
        }
        pending.Clear();
        foreach (Entry entry in entries)
        {
            entry.Status = QuestStatus.Locked;
            Array.Clear(entry.Progress, 0, entry.Progress.Length);
        }
        foreach (GameSaveData.QuestSaveState state in saved)
        {
            Entry entry = Find(state.id);
            entry.Status = state.state;
            foreach (GameSaveData.ObjectiveSaveState objective in state.objectives ?? new GameSaveData.ObjectiveSaveState[0])
                entry.Progress[Array.FindIndex(entry.Definition.Objectives, o => o.id == objective.id)] = objective.progress;
        }
        // A quest added to the data after this save was written opens quietly; nothing is granted.
        ActivateEligible(grant: false, announce: false);
        NotifyChanged();
    }

    // ---- Development ----

    // MissionDebugCube: finish the current main quest as if its objectives were done (rewards included).
    public void DebugCompleteCurrentMain()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Entry main = CurrentMain;
        if (main == null || processing)
        {
            Debug.Log("[개발용] 완료할 진행 중 퀘스트가 없습니다.", this);
            return;
        }
        processing = true;
        try
        {
            for (int i = 0; i < main.ObjectiveCount; i++) main.Progress[i] = main.GetRequired(i);
            Complete(main);
            ActivateEligible(grant: true, announce: true);
            NotifyChanged();
            Debug.Log($"[개발용 퀘스트 완료] {main.Definition.Id} {main.Definition.Title}", this);
        }
        finally { processing = false; }
#endif
    }
}
