using UnityEngine;
using UnityEngine.InputSystem;

// One Esc press closes only the top-most thing. Anything that closes or cancels on Esc
// (TradingPostUI, a build-mode drag) calls Consume(); PauseMenu runs after every other
// Update (DefaultExecutionOrder) and opens or closes only for a press nobody consumed.
public static class UIEscape
{
    private static int consumedFrame = -1;

    public static bool WasPressed => Application.isFocused && Keyboard.current != null &&
        Keyboard.current.escapeKey.wasPressedThisFrame && consumedFrame != Time.frameCount;

    public static void Consume() => consumedFrame = Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => consumedFrame = -1;
}
