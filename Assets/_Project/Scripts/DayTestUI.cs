using TMPro;
using UnityEngine;

public sealed class DayTestUI : MonoBehaviour
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private TMP_Text label;

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
            label.text = $"현재 {gameDay.CurrentDay}일차\n집 문 근처에서 G: 취침";
    }
}
