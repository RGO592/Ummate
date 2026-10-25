using System;
using UnityEngine;

// Shared entry point for short result messages ("목재 3개를 획득했습니다.", "밀이 부족합니다.").
// Any system calls PlayerFeedback.Notify; the active PlayerFeedbackUI in the current scene shows it.
// Roles: GuideTooltip / Tip = what to do next, Interactable.GetInteractionPrompt = "[F] ..." near a target,
// Notify = what just happened. Callers keep their own Debug.Log lines; this does not log.
public static class PlayerFeedback
{
    public static event Action<string> Notified;

    // Runtime-only reset so a previous play session (with domain reload disabled) leaves no listeners.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners() => Notified = null;

    public static void Notify(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || Notified == null) return;
        // Feedback must never break the gameplay action that reported it.
        try { Notified.Invoke(message); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
}
