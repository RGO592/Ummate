using UnityEngine;

// Environment tile kinds placed on the eco grid (숲&강: Water, Tree, Flower, Mushroom, Rock). None marks an empty cell.
// Saved as an int in scenes and save files: never reuse a value. 1 was the retired Wheat Hex (the farm's wheat
// item and crop are unrelated and remain); a save still holding it is rejected rather than read as another kind.
public enum EnvironmentTileType
{
    None = 0,
    Flower = 2,
    Water = 3,
    Rock = 4,
    Tree = 5,
    Mushroom = 6
}

public static class EnvironmentTileTypes
{
    // Selection order used by the 1~5 number keys. Mushroom took the former Wheat slot.
    public static readonly EnvironmentTileType[] All =
    {
        EnvironmentTileType.Mushroom, EnvironmentTileType.Flower, EnvironmentTileType.Water,
        EnvironmentTileType.Rock, EnvironmentTileType.Tree
    };

    public static string DisplayName(EnvironmentTileType type) => type switch
    {
        EnvironmentTileType.Mushroom => "버섯",
        EnvironmentTileType.Flower => "꽃",
        EnvironmentTileType.Water => "물",
        EnvironmentTileType.Rock => "돌",
        EnvironmentTileType.Tree => "나무",
        _ => "없음"
    };

    // Placeholder colours until real prefabs are assigned.
    public static Color DefaultColor(EnvironmentTileType type) => type switch
    {
        EnvironmentTileType.Mushroom => new Color(0.72f, 0.5f, 0.36f),
        EnvironmentTileType.Flower => new Color(0.96f, 0.55f, 0.78f),
        EnvironmentTileType.Water => new Color(0.22f, 0.5f, 0.9f),
        EnvironmentTileType.Rock => new Color(0.6f, 0.6f, 0.62f),
        EnvironmentTileType.Tree => new Color(0.3f, 0.62f, 0.3f),
        _ => Color.white
    };

    // Water sinks the ground column instead of sitting on top of it.
    public static bool IsSunken(EnvironmentTileType type) => type == EnvironmentTileType.Water;
}
