using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameDay : MonoBehaviour
{
    public int CurrentDay { get; private set; } = 1;
    // Raised after CurrentDay changes: previous day, new day.
    public event Action<int, int> DayChanged;
    public event Action StateRestored;
    private bool changingDay;

    // Loading is not a new day: never invoke DayChanged here.
    public void RestoreDay(int day)
    {
        CurrentDay = day;
        StateRestored?.Invoke();
    }

    public void Sleep()
    {
        // Prevent a subscriber from recursively advancing the same sleep operation.
        if (changingDay || CurrentDay == int.MaxValue) return;
        changingDay = true;
        try
        {
            int previousDay = CurrentDay;
            CurrentDay++;
            Debug.Log($"취침 완료: {previousDay}일차 → {CurrentDay}일차", this);
            DayChanged?.Invoke(previousDay, CurrentDay);
        }
        finally { changingDay = false; }
    }
}
