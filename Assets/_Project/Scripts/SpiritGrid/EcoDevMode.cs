using System;
using UnityEngine;

// DEV free placement toggle shared by the DEV button and the grid. OFF by default every play session.
public static class EcoDevMode
{
    public static bool FreePlacement { get; private set; }
    public static event Action Changed;

    public static void SetFreePlacement(bool enabled)
    {
        if (FreePlacement == enabled) return;
        FreePlacement = enabled;
        Changed?.Invoke();
    }

    public static void ToggleFreePlacement() => SetFreePlacement(!FreePlacement);

    // Runtime-only reset so a previous play session (with domain reload disabled) never leaks DEV ON.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        FreePlacement = false;
        Changed = null;
    }
}
