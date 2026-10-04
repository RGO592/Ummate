using UnityEngine;

// Disable this component in the Inspector to remove the cube from G interaction targets.
[DisallowMultipleComponent]
public sealed class MissionDebugInteractable : Interactable
{
    private void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        enabled = false;
#endif
    }

    public override void Interact(GameObject interactor)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!isActiveAndEnabled || interactor == null) return;
        MissionProgress mission = interactor.GetComponent<MissionProgress>();
        if (mission == null)
        {
            Debug.LogWarning("[개발용] 상호작용한 플레이어에 MissionProgress가 없습니다.", this);
            return;
        }
        mission.DebugAdvanceMission();
#endif
    }
}
