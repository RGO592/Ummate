using UnityEngine;

// Put next to a SceneTransitionTrigger: the exit works only once the quest has started (Active or
// Completed), or, with an objective set, once that objective is done. The trigger itself and the travel
// flow are unchanged; the safety wall behind the exit keeps the player in the village while it is closed.
[DisallowMultipleComponent]
public sealed class QuestTravelGate : MonoBehaviour
{
    [SerializeField] private QuestLog quests;
    [SerializeField] private string requiredQuestId = "Q05";
    [SerializeField, Tooltip("Optional. Empty = the quest having started is enough.")]
    private string requiredObjectiveId;
    [SerializeField, TextArea] private string lockedMessage = "아직 숲으로 갈 수 없어.\n마을에서 시험 준비부터 끝내고 가자!";

    // An unassigned log leaves the exit open rather than trapping the player.
    public bool IsOpen => quests == null || (string.IsNullOrEmpty(requiredObjectiveId)
        ? quests.GetStatus(requiredQuestId) != QuestStatus.Locked
        : quests.IsObjectiveDone(requiredQuestId, requiredObjectiveId));

    public void ReportLocked() => PlayerFeedback.Notify(lockedMessage);

    private void Awake()
    {
        if (quests == null) Debug.LogWarning($"{name}: QuestLog가 연결되지 않아 출구 잠금이 꺼져 있습니다.", this);
    }
}
