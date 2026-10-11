using TMPro;
using UnityEngine;

public sealed class DayTestUI : MonoBehaviour
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private TMP_Text label;
    [SerializeField, Min(1)] private int dateFontSize = 32;
    [SerializeField, Min(1)] private int hintFontSize = 20;
    [SerializeField] private PlayerInteractor player;
    [SerializeField] private HouseDoorInteractable houseDoor;
    [SerializeField] private Collider[] doorColliders;
    private bool showingSleepHint;

    private void LateUpdate()
    {
        bool nearby = IsNearDoor();
        if (nearby == showingSleepHint) return;
        showingSleepHint = nearby;
        Refresh();
    }

    private bool IsNearDoor()
    {
        if (player == null || houseDoor == null || !houseDoor.isActiveAndEnabled || doorColliders == null) return false;
        Vector3 origin = player.transform.position;
        float range = player.InteractionDistance;
        foreach (var collider in doorColliders)
        {
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                (collider.ClosestPoint(origin) - origin).sqrMagnitude <= range * range) return true;
        }
        return false;
    }

    private void OnEnable()
    {
        if (gameDay != null) gameDay.DayChanged += OnDayChanged;
        if (gameDay != null) gameDay.StateRestored += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (gameDay != null) gameDay.DayChanged -= OnDayChanged;
        if (gameDay != null) gameDay.StateRestored -= Refresh;
    }

    private void OnDayChanged(int previousDay, int newDay) => Refresh();

    private void Refresh()
    {
        if (gameDay != null && label != null)
            label.text = $"<size={dateFontSize}><b>현재 {gameDay.CurrentDay}일차</b></size>"
                + (showingSleepHint ? $"\n<size={hintFontSize}>집 문 근처에서 F: 취침</size>" : "");
    }
}

