using TMPro;
using UnityEngine;

// Date label only. The door "[F] 잠자기" hint now comes from the shared PlayerFeedbackUI prompt.
public sealed class DayTestUI : MonoBehaviour
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private TMP_Text label;
    [SerializeField, Min(1)] private int dateFontSize = 32;

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
            label.text = $"<size={dateFontSize}><b>현재 {gameDay.CurrentDay}일차</b></size>";
    }
}
