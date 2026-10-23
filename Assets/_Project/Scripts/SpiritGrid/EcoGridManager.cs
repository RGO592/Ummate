using System;
using System.Collections.Generic;
using UnityEngine.Serialization;
using UnityEngine;

// Owns the eco grid: cell data, grid <-> world conversion, and place / move / remove.
// Requests (Can*) are separated from execution (Try*) so cost and pattern rules can hook in later.
// Cell data is independent of the continuous natural ground supplied by the scene.
[DisallowMultipleComponent]
public sealed class EcoGridManager : MonoBehaviour
{
    [Serializable]
    public struct TileSettings
    {
        public EnvironmentTileType type;
        [Tooltip("Optional model. Empty uses the placeholder view. Implement IEcoTileView to react to neighbours.")]
        public GameObject prefab;
        [Min(0), Tooltip("Placeholder gold cost, checked only while DEV free placement is OFF.")]
        public int cost;
        [Min(0), Tooltip("Days of construction outside DEV. 0 completes immediately.")]
        public int buildDays;
    }

    [Header("Grid")]
    [SerializeField, Tooltip("Parent for cell visuals and the origin at Hex(0,0), the first southwest cell centre.")]
    private Transform gridRoot;
    [SerializeField, Min(1)] private int width = 6;
    [SerializeField, Min(1)] private int height = 6;
    [SerializeField, Min(0.5f), FormerlySerializedAs("cellSize")] private float hexWidth = 10f;

    [Header("Surface Presentation")]
    [SerializeField, Min(0f), Tooltip("Inset on exposed edges only; same-type neighbours always meet at the cell boundary.")]
    private float surfaceInset = 0.12f;
    [SerializeField, Tooltip("Solid type-coloured surface for Wheat, Flower, Rock and Tree. Render after ground tint (queue 2002).") ]
    private Material surfaceMaterial;
    [SerializeField, Tooltip("Water surface only. Defaults to Resources/SG_WaterSurface.")]
    private Material waterSurfaceMaterial;

    [Header("Placement")]
    [SerializeField] private TileSettings[] tileSettings =
    {
        new TileSettings { type = EnvironmentTileType.Wheat, cost = 100, buildDays = 1 },
        new TileSettings { type = EnvironmentTileType.Flower, cost = 120, buildDays = 1 },
        new TileSettings { type = EnvironmentTileType.Water, cost = 150, buildDays = 2 },
        new TileSettings { type = EnvironmentTileType.Rock, cost = 80, buildDays = 1 },
        new TileSettings { type = EnvironmentTileType.Tree, cost = 200, buildDays = 3 }
    };

    private Dictionary<HexCoord, EcoGridCell> cells;
    private readonly HashSet<HexCoord> mapHexes = new HashSet<HexCoord>();

    // Raised for every cell whose content or build state changed (both cells on a move).
    // Future ecosystem pattern evaluation and UI can subscribe here.
    public event Action<EcoGridCell> CellChanged;

    public int Width => width;
    public int Height => height;
    public float CellSize => hexWidth;
    public bool DevFreePlacement => EcoDevMode.FreePlacement;
    public Transform GridRoot => gridRoot != null ? gridRoot : transform;
    public int MapColumns => width;
    public int MapRows => height;
    public IEnumerable<HexCoord> MapHexes => mapHexes;
    public IEnumerable<HexCoord> CellCoordinates => cells.Keys;
    private bool boundsReady;
    private Matrix4x4 boundsMatrix;
    private Bounds activeWorldBounds, mapWorldBounds, mapLocalBounds;
    public Bounds ActiveBounds { get { EnsureBounds(); return activeWorldBounds; } }
    public Bounds MapBounds { get { EnsureBounds(); return mapWorldBounds; } }
    public Bounds MapLocalBounds { get { EnsureBounds(); return mapLocalBounds; } }
    private void EnsureBounds()
    {
        EnsureGrid();
        Matrix4x4 matrix = GridRoot.localToWorldMatrix;
        if (boundsReady && matrix == boundsMatrix) return;
        activeWorldBounds = CalculateBounds(CellCoordinates, true);
        mapWorldBounds = CalculateBounds(MapHexes, true);
        mapLocalBounds = CalculateBounds(MapHexes, false);
        boundsMatrix = matrix;
        boundsReady = true;
    }

    private void Awake() => EnsureGrid();

    // Consumers can Awake in any order; data is initialized exactly once.
    public void EnsureGrid()
    {
        if (cells != null) return;
        cells = new Dictionary<HexCoord, EcoGridCell>();
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
            {
                HexCoord coord = HexMetrics.FromOffset(col, row);
                mapHexes.Add(coord);
                cells.Add(coord, new EcoGridCell(coord));
            }
    }

    public bool IsInMap(HexCoord coord) => mapHexes.Contains(coord);
    public bool IsInside(HexCoord coord) => cells != null && cells.ContainsKey(coord);
    public EcoGridCell GetCell(HexCoord coord) => cells != null && cells.TryGetValue(coord, out EcoGridCell cell) ? cell : null;
    public Vector3 GridToWorld(HexCoord coord) => HexMetrics.HexToWorld(GridRoot, coord, hexWidth);

    // Every map coordinate owns a usable cell.
    public bool WorldToGrid(Vector3 world, out HexCoord coord)
    {
        coord = HexMetrics.WorldToHex(GridRoot, world, hexWidth);
        return IsInMap(coord);
    }

    public bool RaycastGrid(Ray ray, out HexCoord coord)
    {
        coord = default;
        var plane = new Plane(GridRoot.up, GridRoot.position);
        return plane.Raycast(ray, out float distance) && WorldToGrid(ray.GetPoint(distance), out coord);
    }

    private Bounds CalculateBounds(IEnumerable<HexCoord> coordinates, bool world)
    {
        bool first = true;
        Bounds result = default;
        foreach (HexCoord coord in coordinates)
            for (int d = 0; d < 6; d++)
            {
                Vector3 point = HexMetrics.LocalCenter(coord, hexWidth) + HexMetrics.Corner(d, hexWidth);
                if (world) point = GridRoot.TransformPoint(point);
                if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                else result.Encapsulate(point);
            }
        return result;
    }

    // ---- Place ----

    public int GetCost(EnvironmentTileType type) => GetSettings(type).cost;

    public bool CanPlace(EnvironmentTileType type, HexCoord coord, out string reason)
    {
        reason = null;
        if (type == EnvironmentTileType.None) { reason = "환경 칸 종류가 선택되지 않았습니다."; return false; }
        EcoGridCell cell = GetCell(coord);
        if (cell == null) { reason = "그리드 범위 밖입니다."; return false; }
        if (!cell.IsEmpty) { reason = "이미 환경물이 있습니다."; return false; }
        // All forest Hex cells use the same placement rules.
        return HasPlacementCost(type, out reason);
    }

    public bool TryPlace(EnvironmentTileType type, HexCoord coord, out string reason)
    {
        if (!CanPlace(type, coord, out reason)) return false;
        // Future: spend gold / materials here (skipped while DEV free placement is ON).
        PlaceTile(type, coord);
        return true;
    }

    // Executes an already validated placement.
    private void PlaceTile(EnvironmentTileType type, HexCoord coord)
    {
        EcoGridCell cell = cells[coord];
        int days = EcoDevMode.FreePlacement ? 0 : GetSettings(type).buildDays;
        cell.SetTile(type, days, CreateTileVisual(type, coord));
        RefreshTerrain();
        CellChanged?.Invoke(cell);
    }

    // DEV ON skips the cost check entirely. OFF is the normal path; the currency system is not connected yet.
    private bool HasPlacementCost(EnvironmentTileType type, out string reason)
    {
        reason = null;
        if (EcoDevMode.FreePlacement) return true;
        // Future: return wallet.Gold >= GetCost(type) (and materials) here.
        reason = $"비용 {GetCost(type)}G가 필요합니다. (재화 시스템 준비 중 · DEV 무료배치를 켜면 설치 가능)";
        return false;
    }

    // ---- Move ----

    public bool CanMove(HexCoord from, HexCoord to, out string reason)
    {
        reason = null;
        EcoGridCell source = GetCell(from);
        EcoGridCell target = GetCell(to);
        if (source == null || source.IsEmpty) { reason = "옮길 환경물이 없습니다."; return false; }
        if (target == null) { reason = "그리드 범위 밖입니다."; return false; }
        if (from == to) { reason = "같은 칸입니다."; return false; }
        if (!target.IsEmpty) { reason = "이미 환경물이 있습니다."; return false; }
        return true;
    }

    public bool TryMove(HexCoord from, HexCoord to, out string reason)
    {
        if (!CanMove(from, to, out reason)) return false;
        EcoGridCell source = cells[from];
        EcoGridCell target = cells[to];
        // Build progress moves with the tile.
        target.TakeFrom(source);
        if (target.Visual != null)
        {
            target.Visual.transform.position = GridToWorld(to);
            target.Visual.name = VisualName(target.TileType, to);
            EcoPlaceholderTileView placeholder = target.Visual.GetComponent<EcoPlaceholderTileView>();
            if (placeholder != null) placeholder.SetCoord(to);
        }
        RefreshTerrain();
        CellChanged?.Invoke(source);
        CellChanged?.Invoke(target);
        return true;
    }

    // ---- Remove ----

    public bool CanRemove(HexCoord coord, out string reason)
    {
        reason = null;
        EcoGridCell cell = GetCell(coord);
        if (cell == null || cell.IsEmpty) { reason = "제거할 환경물이 없습니다."; return false; }
        return true;
    }

    public bool TryRemove(HexCoord coord, out string reason)
    {
        if (!CanRemove(coord, out reason)) return false;
        EcoGridCell cell = cells[coord];
        if (cell.Visual != null) Destroy(cell.Visual);
        cell.Clear();
        RefreshTerrain();
        CellChanged?.Invoke(cell);
        return true;
    }

    // ---- Construction (not connected to GameDay yet) ----

    // Call once per in-game day when day progression is connected.
    public void AdvanceConstructionDay()
    {
        if (cells == null) return;
        foreach (EcoGridCell cell in cells.Values)
        {
            if (cell.State != BuildState.Constructing) continue;
            cell.AdvanceBuildDay();
            CellChanged?.Invoke(cell);
        }
        RefreshTerrain();
    }

    // ---- Connections and ground ----

    public HexDirectionMask GetNeighborMask(HexCoord coord)
    {
        EcoGridCell cell = GetCell(coord);
        if (cell == null || cell.IsEmpty) return HexDirectionMask.None;
        HexDirectionMask mask = HexDirectionMask.None;
        for (int d = 0; d < HexCoord.Directions.Count; d++)
            if (SameType(coord.Neighbor(d), cell.TileType)) mask |= (HexDirectionMask)(1 << d);
        return mask;
    }

    private bool SameType(HexCoord coord, EnvironmentTileType type)
    {
        EcoGridCell other = GetCell(coord);
        return other != null && !other.IsEmpty && other.TileType == type;
    }

    // Refreshes view connections after placement, moving, removal or build-state changes.
    private void RefreshTerrain()
    {
        foreach (EcoGridCell cell in cells.Values)
        {
            if (cell.Visual == null) continue;
            IEcoTileView view = cell.Visual.GetComponent<IEcoTileView>();
            if (view == null) continue;
            view.SetBuildState(cell.State);
            view.SetNeighbors(GetNeighborMask(cell.Coord));
        }
    }

    // ---- Visuals ----

    private TileSettings GetSettings(EnvironmentTileType type)
    {
        foreach (TileSettings s in tileSettings)
            if (s.type == type) return s;
        return new TileSettings { type = type };
    }

    private GameObject CreateTileVisual(EnvironmentTileType type, HexCoord coord)
    {
        GameObject prefab = GetSettings(type).prefab;
        if (prefab != null)
        {
            GameObject go = Instantiate(prefab, GridRoot, false);
            go.transform.localPosition = HexMetrics.LocalCenter(coord, hexWidth);
            go.name = VisualName(type, coord);
            return go;
        }
        var pivot = new GameObject(VisualName(type, coord));
        pivot.transform.SetParent(GridRoot, false);
        pivot.transform.localPosition = HexMetrics.LocalCenter(coord, hexWidth);
        pivot.AddComponent<EcoPlaceholderTileView>().Build(type, coord, hexWidth, surfaceInset, waterSurfaceMaterial, surfaceMaterial);
        return pivot;
    }

    private static string VisualName(EnvironmentTileType type, HexCoord coord) => $"Tile_{type}_{coord.q}_{coord.r}";

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.matrix = GridRoot.localToWorldMatrix;
        for (int col = 0; col < width; col++)
            for (int row = 0; row < height; row++)
            {
                Vector3 center = HexMetrics.LocalCenter(HexMetrics.FromOffset(col, row), hexWidth);
                for (int d = 0; d < 6; d++)
                    Gizmos.DrawLine(center + HexMetrics.Corner(d, hexWidth), center + HexMetrics.Corner((d + 1) % 6, hexWidth));
            }
        Gizmos.matrix = Matrix4x4.identity;
    }

}
