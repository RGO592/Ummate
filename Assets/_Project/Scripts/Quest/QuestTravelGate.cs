using UnityEngine;

// Put next to a SceneTransitionTrigger: the exit works only once the quest has started (Active or
// Completed). The trigger itself and the travel flow are unchanged; the safety wall behind the exit
// keeps the player in the village while it is closed.
[DisallowMultipleComponent]
public sealed class QuestTravelGate : MonoBehaviour
{
    [SerializeField] private QuestLog quests;
    [SerializeField] private string requiredQuestId = "Q05";
    [SerializeField, TextArea] private string lockedMessage = "아직 숲으로 갈 수 없어. 마을에서 시험 준비부터 끝내자!";

    // An unassigned log leaves the exit open rather than trapping the player.
    public bool IsOpen => quests == null || quests.GetStatus(requiredQuestId) != QuestStatus.Locked;

    public void ReportLocked() => PlayerFeedback.Notify(lockedMessage);

    private void Awake()
    {
        if (quests == null) Debug.LogWarning($"{name}: QuestLog가 연결되지 않아 출구 잠금이 꺼져 있습니다.", this);
    }
}
