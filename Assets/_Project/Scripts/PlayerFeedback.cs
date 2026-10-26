using System;
using System.Collections.Generic;
using UnityEngine;

// Shared entry point for short result messages ("목재 3개를 획득했습니다.", "밀이 부족합니다.").
// Any system calls PlayerFeedback.Notify; the active PlayerFeedbackUI in the current scene shows it.
// Roles: GuideTooltip / Tip = what to do next, Interactable.GetInteractionPrompt = "[F] ..." near a target,
// Notify = what just happened. Callers keep their own Debug.Log lines; this does not log.
// NotifyUntilClick = a story line that stays until the player left-clicks it away (one at a time, in order).
public static class PlayerFeedback
{
    public static event Action<string> Notified;
    public static event Action UntilClickChanged;

    // Held here, not in the view, so a line raised while no scene shows feedback (or while the
    // showing scene is suspended for travel) is still waiting when a PlayerFeedbackUI comes up.
    private static readonly Queue<string> untilClick = new Queue<string>();

    public static string CurrentUntilClick => untilClick.Count > 0 ? untilClick.Peek() : null;

    // Runtime-only reset so a previous play session (with domain reload disabled) leaves no listeners.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners()
    {
        Notified = null;
        UntilClickChanged = null;
        untilClick.Clear();
    }

    public static void Notify(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || Notified == null) return;
        // Feedback must never break the gameplay action that reported it.
        try { Notified.Invoke(message); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    public static void NotifyUntilClick(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        untilClick.Enqueue(message);
        RaiseUntilClickChanged();
    }

    // Called by the view when the player clicks the current line away.
    public static void DismissUntilClick()
    {
        if (untilClick.Count == 0) return;
        untilClick.Dequeue();
        RaiseUntilClickChanged();
    }

    // A new game or a loaded save starts without lines left over from the previous run.
    public static void ClearUntilClick()
    {
        if (untilClick.Count == 0) return;
        untilClick.Clear();
        RaiseUntilClickChanged();
    }

    private static void RaiseUntilClickChanged()
    {
        try { UntilClickChanged?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
}
