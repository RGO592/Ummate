using UnityEngine;

// PlayerPrefs keys for the player's options (display, audio). Separate from the game save slots.
// The project used to store them under its former code name; those values are copied once to the
// current keys (only when the current key does not exist yet) and are never read again afterwards.
public static class SettingsKeys
{
    public const string Prefix = "Susupjung.";

    // The former prefix, used by the one-time copy below and nowhere else.
    private const string LegacyPrefix = "Ummate.";
    private static readonly string[] LegacyIntKeys = { "Display.Width", "Display.Height", "Display.Mode" };
    private static readonly string[] LegacyFloatKeys =
        { "Audio.MasterVolume", "Audio.BgmVolume", "Audio.SfxVolume", "Audio.AmbienceVolume" };
    private static bool migrated;

    // Call before reading any option. Cheap after the first call. Old keys are left in place (never deleted).
    public static void MigrateLegacy()
    {
        if (migrated) return;
        migrated = true;
        bool changed = false;
        foreach (string name in LegacyIntKeys)
            if (CanCopy(name)) { PlayerPrefs.SetInt(Prefix + name, PlayerPrefs.GetInt(LegacyPrefix + name)); changed = true; }
        foreach (string name in LegacyFloatKeys)
            if (CanCopy(name)) { PlayerPrefs.SetFloat(Prefix + name, PlayerPrefs.GetFloat(LegacyPrefix + name)); changed = true; }
        if (changed) PlayerPrefs.Save();
    }

    private static bool CanCopy(string name) => !PlayerPrefs.HasKey(Prefix + name) && PlayerPrefs.HasKey(LegacyPrefix + name);
}
