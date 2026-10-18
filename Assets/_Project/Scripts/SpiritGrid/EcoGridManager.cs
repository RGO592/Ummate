using System;
using UnityEngine;

// Owns the eco grid: cell data, grid <-> world conversion, and place / move / remove.
// Requests (Can*) are separated from execution (Try*) so cost, lock and pattern rules can hook in later.
// Each cell has a ground column (top at the grid plane); sunken tiles such as water lower it.
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
    [SerializeField, Tooltip("Parent for cell visuals and the grid origin (centre of the grid, top of the ground).")]
    private Transform gridRoot;
    [SerializeField, Min(1)] private int width = 6;
    [SerializeField, Min(1)] private int height = 6;
    [SerializeField, Min(0.5f)] private float cellSize = 10f;

    [Header("Ground Columns")]
    [SerializeField, Range(0.85f, 1f), Tooltip("Column width per cell. The gap reads as grid lines.")]
    private float columnFill = 0.98f;
    [SerializeField, Min(0.2f)] private float columnDepth = 1.2f;
    [SerializeField, Min(0.1f), Tooltip("How far a sunken tile (water) lowers its column.")]
    private float sunkenDepth = 0.8f;
    [SerializeField] private Color columnColor = new Color(0.74f, 0.68f, 0.52f);
    [SerializeField] private Color groundFloorColor = new Color(0.32f, 0.24f, 0.16f);

    [Header("Placement")]
    [SerializeField] private TileSettings[] tileSettings =
    {
        new TileSettings { type = EnvironmentTileType.Wheat, cost = 100, buildDays = 1 },
        new TileSettings { type = EnvironmentTileType.Flower, cost = 120, buildDays = 1 },
        new TileSettings { type = EnvironmentTileType.Water, cost = 150, buildDays = 2 },
        new TileSettings { type = EnvironmentTileType.Rock, cost = 80, buildDays = 1 },
        new TileSettings { type = EnvironmentTileType.Tree, cost = 200, buildDays = 3 }
    };

    private EcoGridCell[,] cells;
    private Transform[,] columns;

    // Raised for every cell whose content or build state changed (both cells on a move).
    // Future ecosystem pattern evaluation and UI can subscribe here.
    public event Action<EcoGridCell> CellChanged;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public bool DevFreePlacement => EcoDevMode.FreePlacement;
    public Transform GridRoot => gridRoot != null ? gridRoot : transform;
    // Half extents of the grid on the ground plane, for cameras and surroundings.
    public Vector2 HalfExtents => new Vector2(width * cellSize * 0.5f, height * cellSize * 0.5f);

    private void Awake()
    {
        BuildGrid();
    }

    private void BuildGrid()
    {
        cells = new EcoGridCell[width, height];
        columns = new Transform[width, height];
        Transform root = GridRoot;
        var groundRoot = new GameObject("GridGround").transform;
        groundRoot.SetParent(root, false);
        // Dark floor under the columns shows through the gaps as grid lines.
        Vector2 half = HalfExtents;
        EcoVisualUtil.Box(groundRoot, "GridFloor",
            new Vector3(-half.x, -columnDepth - 0.05f, -half.y), new Vector3(half.x, -columnDepth + 0.02f, half.y), groundFloorColor, true);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var coord = new Vector2Int(x, y);
                cells[x, y] = new EcoGridCell(coord);
                Vector3 c = LocalCenter(coord);
                float h = cellSize * columnFill * 0.5f;
                GameObject column = EcoVisualUtil.Box(groundRoot, $"Column_{x}_{y}",
                    new Vector3(c.x - h, -columnDepth, c.z - h), new Vector3(c.x + h, 0f, c.z + h), columnColor, true);
                columns[x, y] = column.transform;
                cells[x, y].BaseVisual = column;
            }
    }

    // ---- Coordinates ----

    public bool IsInside(Vector2Int coord) => coord.x >= 0 && coord.y >= 0 && coord.x < width && coord.y < height;

    public EcoGridCell GetCell(Vector2Int coord) => cells != null && IsInside(coord) ? cells[coord.x, coord.y] : null;

    private Vector3 LocalCenter(Vector2Int coord) =>
        new Vector3((coord.x - (width - 1) * 0.5f) * cellSize, 0f, (coord.y - (height - 1) * 0.5f) * cellSize);

    // Centre of the cell on the grid plane.
    public Vector3 GridToWorld(Vector2Int coord)
    {
        Transform root = GridRoot;
        return root.position + root.rotation * LocalCenter(coord);
    }

    public bool WorldToGrid(Vector3 world, out Vector2Int coord)
    {
        Transform root = GridRoot;
        Vector3 local = Quaternion.Inverse(root.rotation) * (world - root.position);
        coord = new Vector2Int(
            Mathf.FloorToInt(local.x / cellSize + width * 0.5f),
            Mathf.FloorToInt(local.z / cellSize + height * 0.5f));
        return IsInside(coord);
    }

    // Intersects a ray with the grid plane, ignoring any colliders.
    public bool RaycastGrid(Ray ray, out Vector2Int coord)
    {
        coord = default;
        var plane = new Plane(GridRoot.up, GridRoot.position);
        return plane.Raycast(ray, out float distance) && WorldToGrid(ray.GetPoint(distance), out coord);
    }

    // ---- Place ----

    public int GetCost(EnvironmentTileType type) => GetSettings(type).cost;

    public bool CanPlace(EnvironmentTileType type, Vector2Int coord, out string reason)
    {
        reason = null;
        if (type == EnvironmentTileType.None) { reason = "환경 칸 종류가 선택되지 않았습니다."; return false; }
        EcoGridCell cell = GetCell(coord);
        if (cell == null) { reason = "그리드 범위 밖입니다."; return false; }
        if (!cell.IsEmpty) { reason = "이미 환경물이 있습니다."; return false; }
        // Future: locked grid areas are rejected here.
        return HasPlacementCost(type, out reason);
    }

    public bool TryPlace(EnvironmentTileType type, Vector2Int coord, out string reason)
    {
        if (!CanPlace(type, coord, out reason)) return false;
        // Future: spend gold / materials here (skipped while DEV free placement is ON).
        PlaceTile(type, coord);
        return true;
    }

    // Executes an already validated placement.
    private void PlaceTile(EnvironmentTileType type, Vector2Int coord)
    {
        EcoGridCell cell = cells[coord.x, coord.y];
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

    public bool CanMove(Vector2Int from, Vector2Int to, out string reason)
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

    public bool TryMove(Vector2Int from, Vector2Int to, out string reason)
    {
        if (!CanMove(from, to, out reason)) return false;
        EcoGridCell source = cells[from.x, from.y];
        EcoGridCell target = cells[to.x, to.y];
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

    public bool CanRemove(Vector2Int coord, out string reason)
    {
        reason = null;
        EcoGridCell cell = GetCell(coord);
        if (cell == null || cell.IsEmpty) { reason = "제거할 환경물이 없습니다."; return false; }
        return true;
    }

    public bool TryRemove(Vector2Int coord, out string reason)
    {
        if (!CanRemove(coord, out reason)) return false;
        EcoGridCell cell = cells[coord.x, coord.y];
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
        foreach (EcoGridCell cell in cells)
        {
            if (cell.State != BuildState.Constructing) continue;
            cell.AdvanceBuildDay();
            CellChanged?.Invoke(cell);
        }
        RefreshTerrain();
    }

    // ---- Connections and ground ----

    // Same-type orthogonal neighbours of a cell (diagonals never connect).
    public EcoNeighborMask GetNeighborMask(Vector2Int coord)
    {
        EcoGridCell cell = GetCell(coord);
        if (cell == null || cell.IsEmpty) return EcoNeighborMask.None;
        EcoNeighborMask mask = EcoNeighborMask.None;
        if (SameType(coord + Vector2Int.up, cell.TileType)) mask |= EcoNeighborMask.North;
        if (SameType(coord + Vector2Int.right, cell.TileType)) mask |= EcoNeighborMask.East;
        if (SameType(coord + Vector2Int.down, cell.TileType)) mask |= EcoNeighborMask.South;
        if (SameType(coord + Vector2Int.left, cell.TileType)) mask |= EcoNeighborMask.West;
        return mask;
    }

    private bool SameType(Vector2Int coord, EnvironmentTileType type)
    {
        EcoGridCell other = GetCell(coord);
        return other != null && !other.IsEmpty && other.TileType == type;
    }

    // Recomputes every cell (36 cells, cheap): column depth, neighbour mask and build state of views.
    private void RefreshTerrain()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                EcoGridCell cell = cells[x, y];
                bool sunken = !cell.IsEmpty && EnvironmentTileTypes.IsSunken(cell.TileType);
                SetColumnTop(columns[x, y], sunken ? -sunkenDepth : 0f);
                if (cell.Visual == null) continue;
                IEcoTileView view = cell.Visual.GetComponent<IEcoTileView>();
                if (view == null) continue;
                view.SetBuildState(cell.State);
                view.SetNeighbors(GetNeighborMask(cell.Coord));
            }
    }

    private void SetColumnTop(Transform column, float top)
    {
        float bottom = -columnDepth;
        Vector3 p = column.localPosition, s = column.localScale;
        column.localPosition = new Vector3(p.x, (top + bottom) * 0.5f, p.z);
        column.localScale = new Vector3(s.x, Mathf.Max(0.01f, top - bottom), s.z);
    }

    // ---- Visuals ----

    private TileSettings GetSettings(EnvironmentTileType type)
    {
        foreach (TileSettings s in tileSettings)
            if (s.type == type) return s;
        return new TileSettings { type = type };
    }

    private GameObject CreateTileVisual(EnvironmentTileType type, Vector2Int coord)
    {
        GameObject prefab = GetSettings(type).prefab;
        if (prefab != null)
        {
            GameObject go = Instantiate(prefab, GridToWorld(coord), GridRoot.rotation, GridRoot);
            go.name = VisualName(type, coord);
            return go;
        }
        var pivot = new GameObject(VisualName(type, coord));
        pivot.transform.SetParent(GridRoot, false);
        pivot.transform.SetPositionAndRotation(GridToWorld(coord), GridRoot.rotation);
        float columnInset = cellSize * (1f - columnFill) * 0.5f;
        pivot.AddComponent<EcoPlaceholderTileView>().Build(type, coord, cellSize, columnInset, sunkenDepth);
        return pivot;
    }

    private static string VisualName(EnvironmentTileType type, Vector2Int coord) => $"Tile_{type}_{coord.x}_{coord.y}";

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
    }

    private void OnDrawGizmos()
    {
        Transform root = GridRoot;
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.matrix = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
        for (int x = 0; x <= width; x++)
        {
            float px = (x - width * 0.5f) * cellSize;
            Gizmos.DrawLine(new Vector3(px, 0.02f, -height * 0.5f * cellSize), new Vector3(px, 0.02f, height * 0.5f * cellSize));
        }
        for (int y = 0; y <= height; y++)
        {
            float pz = (y - height * 0.5f) * cellSize;
            Gizmos.DrawLine(new Vector3(-width * 0.5f * cellSize, 0.02f, pz), new Vector3(width * 0.5f * cellSize, 0.02f, pz));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
