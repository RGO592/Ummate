using UnityEngine;

// Holds drag intent only. The real tile and its neighbours remain untouched until a valid drop.
public sealed class EcoDragSession
{
    public bool IsActive { get; private set; }
    public bool IsMoving { get; private set; }
    public EnvironmentTileType Type { get; private set; }
    public Vector2Int Source { get; private set; }

    public void BeginPlacement(EnvironmentTileType type)
    {
        Cancel();
        if (type == EnvironmentTileType.None) return;
        Type = type;
        IsActive = true;
    }

    public void BeginMove(EnvironmentTileType type, Vector2Int source)
    {
        BeginPlacement(type);
        IsMoving = IsActive;
        Source = source;
    }

    public bool CanDrop(EcoGridManager grid, bool insideGrid, Vector2Int target, out string reason)
    {
        reason = null;
        if (!IsActive || grid == null) { reason = "드래그 중인 환경물이 없습니다."; return false; }
        if (!insideGrid) { reason = "배치 영역 밖입니다."; return false; }
        return IsMoving ? grid.CanMove(Source, target, out reason) : grid.CanPlace(Type, target, out reason);
    }

    public bool Drop(EcoGridManager grid, bool insideGrid, Vector2Int target, out string reason)
    {
        try
        {
            if (!CanDrop(grid, insideGrid, target, out reason)) return false;
            // Revalidation inside Try* protects against a changed cell or DEV state during the drag.
            return IsMoving ? grid.TryMove(Source, target, out reason) : grid.TryPlace(Type, target, out reason);
        }
        finally { Cancel(); }
    }

    public void Cancel()
    {
        IsActive = false;
        IsMoving = false;
        Type = EnvironmentTileType.None;
        Source = default;
    }
}
