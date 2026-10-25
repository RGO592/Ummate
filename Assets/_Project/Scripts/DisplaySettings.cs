using System.Collections.Generic;
using UnityEngine;

// Screen options only (resolution + full screen / windowed), kept in PlayerPrefs apart from the game save.
// The aspect ratio is simply that of the chosen resolution.
public static class DisplaySettings
{
    private const string WidthKey = "Ummate.Display.Width";
    private const string HeightKey = "Ummate.Display.Height";
    private const string ModeKey = "Ummate.Display.Mode";

    // "전체 화면" is Unity's borderless full-screen window; exclusive mode is not offered.
    public static readonly FullScreenMode[] Modes = { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
    public static readonly string[] ModeNames = { "전체 화면", "창 모드" };

    public static bool HasSaved => PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(HeightKey) && PlayerPrefs.HasKey(ModeKey);

    // Supported sizes of the current monitor, one entry per width × height (refresh rates merged), largest first.
    public static List<Vector2Int> AvailableResolutions()
    {
        var sizes = new List<Vector2Int>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            var size = new Vector2Int(resolution.width, resolution.height);
            if (size.x > 0 && size.y > 0 && !sizes.Contains(size)) sizes.Add(size);
        }
        // The Editor Game view may report nothing useful; the current size is always selectable.
        Vector2Int current = CurrentResolution;
        if (!sizes.Contains(current)) sizes.Add(current);
        sizes.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
        return sizes;
    }

    public static Vector2Int CurrentResolution => HasSaved
        ? new Vector2Int(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey))
        : new Vector2Int(Screen.width, Screen.height);

    public static int CurrentModeIndex
    {
        get
        {
            FullScreenMode mode = HasSaved ? (FullScreenMode)PlayerPrefs.GetInt(ModeKey) : Screen.fullScreenMode;
            return mode == FullScreenMode.Windowed ? 1 : 0;
        }
    }

    public static string Describe(Vector2Int size) => $"{size.x} × {size.y}";

    public static void Apply(Vector2Int size, int modeIndex)
    {
        FullScreenMode mode = Modes[Mathf.Clamp(modeIndex, 0, Modes.Length - 1)];
        Screen.SetResolution(size.x, size.y, mode);
        PlayerPrefs.SetInt(WidthKey, size.x);
        PlayerPrefs.SetInt(HeightKey, size.y);
        PlayerPrefs.SetInt(ModeKey, (int)mode);
        PlayerPrefs.Save();
    }

    // Re-applies the player's choice on every launch. Runtime only (no Editor hook).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplySaved()
    {
        if (!HasSaved) return;
        int width = PlayerPrefs.GetInt(WidthKey), height = PlayerPrefs.GetInt(HeightKey);
        if (width <= 0 || height <= 0) return;
        FullScreenMode mode = (FullScreenMode)PlayerPrefs.GetInt(ModeKey);
        if (mode != FullScreenMode.Windowed) mode = FullScreenMode.FullScreenWindow;
        Screen.SetResolution(width, height, mode);
    }
}
