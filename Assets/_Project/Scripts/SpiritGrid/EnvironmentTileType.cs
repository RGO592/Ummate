using UnityEngine;

// Environment tile kinds placed on the eco grid. None marks an empty cell.
public enum EnvironmentTileType
{
    None = 0,
    Wheat = 1,
    Flower = 2,
    Water = 3,
    Rock = 4,
    Tree = 5
}

public static class EnvironmentTileTypes
{
    // Selection order used by the 1~5 number keys and the build bar.
    public static readonly EnvironmentTileType[] All =
    {
        EnvironmentTileType.Wheat, EnvironmentTileType.Flower, EnvironmentTileType.Water,
        EnvironmentTileType.Rock, EnvironmentTileType.Tree
    };

    public static string DisplayName(EnvironmentTileType type) => type switch
    {
        EnvironmentTileType.Wheat => "밀",
        EnvironmentTileType.Flower => "꽃",
        EnvironmentTileType.Water => "물",
        EnvironmentTileType.Rock => "돌",
        EnvironmentTileType.Tree => "나무",
        _ => "없음"
    };

    // Placeholder colours until real prefabs are assigned.
    public static Color DefaultColor(EnvironmentTileType type) => type switch
    {
        EnvironmentTileType.Wheat => new Color(0.95f, 0.8f, 0.3f),
        EnvironmentTileType.Flower => new Color(0.96f, 0.55f, 0.78f),
        EnvironmentTileType.Water => new Color(0.22f, 0.5f, 0.9f),
        EnvironmentTileType.Rock => new Color(0.6f, 0.6f, 0.62f),
        EnvironmentTileType.Tree => new Color(0.3f, 0.62f, 0.3f),
        _ => Color.white
    };

    // Water sinks the ground column instead of sitting on top of it.
    public static bool IsSunken(EnvironmentTileType type) => type == EnvironmentTileType.Water;
}
