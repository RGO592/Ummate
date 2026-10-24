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
    public struct ItemCost
    {
        public ItemDefinition item;
        [Min(1)] public int amount;
    }

    [Serializable]
    public struct TileSettings
    {
        public EnvironmentTileType type;
        [Tooltip("Optional model. Empty uses the placeholder view. Implement IEcoTileView to react to neighbours.")]
        public GameObject prefab;
        [Min(0), Tooltip("신규 설치 골드 비용 (임시 테스트 값). DEV 무료배치 ON이면 무시합니다.")]
        public int cost;
        [Tooltip("신규 설치 재료 비용 (임시 테스트 값). 플레이어 인벤토리에서 차감합니다.")]
        public ItemCost[] itemCosts;
    }

    [Header("Save")]
    [SerializeField, Tooltip("Stable save key for this region's grid. Future regions use their own key.")]
    private string saveId = "ForestRiver";

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
        // Temporary test gold only; item costs are assigned in the scene Inspector.
        new TileSettings { type = EnvironmentTileType.Wheat, cost = 20 },
        new TileSettings { type = EnvironmentTileType.Flower, cost = 30 },
        new TileSettings { type = EnvironmentTileType.Water, cost = 30 },
        new TileSettings { type = EnvironmentTileType.Rock, cost = 30 },
        new TileSettings { type = EnvironmentTileType.Tree, cost = 20 }
    };

    private Dictionary<HexCoord, EcoGridCell> cells;
    private readonly HashSet<HexCoord> mapHexes = new HashSet<HexCoord>();
    private PlayerWallet payerWallet;
    private PlayerInventory payerInventory;
    private int payerSearchFrame = -1;

    // Raised for every cell whose content changed (both cells on a move).
    // Future ecosystem pattern evaluation and UI can subscribe here.
    public event Action<EcoGridCell> CellChanged;
    // Raised once after a save restore replaced the whole grid (no per-cell CellChanged during restore).
    public event Action GridRestored;

    // Save bridge. GameSaveSystem lives in FarmScene; this scene may not be loaded yet when F9 is pressed.
    // Live grids register for their lifetime (they stay registered while the scene is suspended);
    // states for grids that are not loaded wait here and are applied when that grid awakes.
    private static readonly List<EcoGridManager> liveGrids = new List<EcoGridManager>();
    private static readonly Dictionary<string, GameSaveData.HexCellState[]> pendingStates =
        new Dictionary<string, GameSaveData.HexCellState[]>();

    // Runtime-only reset so a previous play session (with domain reload disabled) never leaks saved cells.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSaveBridge()
    {
        liveGrids.Clear();
        pendingStates.Clear();
    }

    public int Width => width;
    public int Height => height;
    public float CellSize => hexWidth;
    public bool DevFreePlacement => EcoDevMode.FreePlacement;
    // What the last successful TryPlace charged, for the status line ("20G · 밀 ×1", "DEV 무료").
    public string LastChargeText { get; private set; } = "";
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

    public string SaveId => string.IsNullOrWhiteSpace(saveId) ? name : saveId;

    private void Awake()
    {
        EnsureGrid();
        if (FindLiveGrid(SaveId) != null) Debug.LogWarning($"중복 Hex Grid 저장 ID: {SaveId}", this);
        liveGrids.Add(this);
        if (pendingStates.TryGetValue(SaveId, out GameSaveData.HexCellState[] pending))
        {
            pendingStates.Remove(SaveId);
            RestoreState(pending);
        }
    }

    private void OnDestroy() => liveGrids.Remove(this);

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

    // One transaction: validate cell + cost, charge, place. A failed placement refunds the charge.
    public bool TryPlace(EnvironmentTileType type, HexCoord coord, out string reason)
    {
        if (!CanPlace(type, coord, out reason)) return false;
        bool free = EcoDevMode.FreePlacement;
        if (!free && !TryChargePlacement(type, out reason)) return false;
        try { PlaceTile(type, coord); }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            // A listener may throw after the tile was committed; then the charge stands.
            EcoGridCell cell = GetCell(coord);
            if (cell != null && !cell.IsEmpty && cell.TileType == type) { LastChargeText = free ? "DEV 무료" : DescribeCost(type); return true; }
            if (!free) RefundPlacement(type);
            reason = "설치 중 오류가 발생해 비용을 돌려받았습니다.";
            return false;
        }
        LastChargeText = free ? "DEV 무료" : DescribeCost(type);
        return true;
    }

    // Executes an already validated placement. Tiles are complete immediately (no construction days).
    private void PlaceTile(EnvironmentTileType type, HexCoord coord)
    {
        EcoGridCell cell = cells[coord];
        cell.SetTile(type, CreateTileVisual(type, coord));
        RefreshTerrain();
        CellChanged?.Invoke(cell);
    }

    // ---- Placement cost (new placements only; moving is free, removal refunds are not defined yet) ----

    // Build bar label, e.g. "20G · 밀 ×1".
    public string DescribeCost(EnvironmentTileType type)
    {
        var parts = new List<string>();
        int gold = GetCost(type);
        if (gold > 0) parts.Add($"{gold}G");
        foreach (KeyValuePair<ItemDefinition, int> entry in GetItemCosts(type))
            parts.Add($"{entry.Key.DisplayName} ×{entry.Value}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "무료";
    }

    // Duplicate entries for the same item are summed so the check matches what is charged.
    private Dictionary<ItemDefinition, int> GetItemCosts(EnvironmentTileType type)
    {
        var totals = new Dictionary<ItemDefinition, int>();
        ItemCost[] costs = GetSettings(type).itemCosts;
        if (costs == null) return totals;
        foreach (ItemCost cost in costs)
        {
            if (cost.item == null || cost.amount <= 0) continue;
            totals.TryGetValue(cost.item, out int current);
            totals[cost.item] = current + cost.amount;
        }
        return totals;
    }

    // The farm player's wallet and inventory. FarmScene stays loaded (inactive) while this scene is visited,
    // the same way SpiritCommonHUD reads its GameDay. Opening this scene alone finds no payer.
    private bool ResolvePayer()
    {
        if (payerWallet != null && payerInventory != null) return true;
        payerWallet = null;
        payerInventory = null;
        if (payerSearchFrame == Time.frameCount) return false;
        payerSearchFrame = Time.frameCount;
        foreach (PlayerWallet wallet in FindObjectsByType<PlayerWallet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            PlayerInventory inventory = wallet.GetComponent<PlayerInventory>();
            if (inventory == null) continue;
            payerWallet = wallet;
            payerInventory = inventory;
            return true;
        }
        return false;
    }

    // DEV ON skips the cost check entirely. OFF checks gold and every material before anything is charged.
    private bool HasPlacementCost(EnvironmentTileType type, out string reason)
    {
        reason = null;
        if (EcoDevMode.FreePlacement) return true;
        int gold = GetCost(type);
        Dictionary<ItemDefinition, int> items = GetItemCosts(type);
        if (gold <= 0 && items.Count == 0) return true;
        if (!ResolvePayer()) { reason = "재화 정보를 찾을 수 없습니다 (숲 마을에서 이동해 오세요)"; return false; }
        var missing = new List<string>();
        if (!payerWallet.HasGold(gold)) missing.Add($"골드 부족 ({gold}G 필요)");
        foreach (KeyValuePair<ItemDefinition, int> entry in items)
            if (payerInventory.GetQuantity(entry.Key) < entry.Value)
                missing.Add($"{entry.Key.DisplayName} 부족 ({entry.Value}개 필요)");
        if (missing.Count == 0) return true;
        reason = string.Join(", ", missing);
        return false;
    }

    // Charges gold, then each material. Any failure returns everything already taken.
    private bool TryChargePlacement(EnvironmentTileType type, out string reason)
    {
        if (!HasPlacementCost(type, out reason)) return false;
        int gold = GetCost(type);
        Dictionary<ItemDefinition, int> items = GetItemCosts(type);
        if (gold <= 0 && items.Count == 0) return true;
        // Wallet / inventory listeners run inside each call; success is judged by the committed balance.
        bool goldTaken = false;
        if (gold > 0)
        {
            int before = payerWallet.Gold;
            try { goldTaken = payerWallet.TrySpendGold(gold); }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                goldTaken = payerWallet.Gold == before - gold;
            }
        }
        bool success = gold <= 0 || goldTaken;
        var itemsTaken = new List<KeyValuePair<ItemDefinition, int>>();
        if (success)
            foreach (KeyValuePair<ItemDefinition, int> entry in items)
            {
                int before = payerInventory.GetQuantity(entry.Key);
                bool taken;
                try { taken = payerInventory.TryConsume(entry.Key, entry.Value); }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    taken = payerInventory.GetQuantity(entry.Key) == before - entry.Value;
                }
                if (!taken) { success = false; break; }
                itemsTaken.Add(entry);
            }
        if (success) return true;
        if (goldTaken) payerWallet.AddGold(gold);
        foreach (KeyValuePair<ItemDefinition, int> entry in itemsTaken) payerInventory.AddItem(entry.Key, entry.Value);
        reason = "비용 차감에 실패했습니다. 차감된 자원은 돌려받았습니다.";
        return false;
    }

    private void RefundPlacement(EnvironmentTileType type)
    {
        if (!ResolvePayer()) return;
        int gold = GetCost(type);
        if (gold > 0) payerWallet.AddGold(gold);
        foreach (KeyValuePair<ItemDefinition, int> entry in GetItemCosts(type)) payerInventory.AddItem(entry.Key, entry.Value);
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

    // ---- Save / load ----

    // Placed gameplay cells only, sorted for a stable file. Background hexes are views, not cells.
    public GameSaveData.HexCellState[] CaptureState()
    {
        EnsureGrid();
        var saved = new List<GameSaveData.HexCellState>();
        foreach (EcoGridCell cell in cells.Values)
            if (!cell.IsEmpty)
                saved.Add(new GameSaveData.HexCellState { q = cell.Coord.q, r = cell.Coord.r, tileType = cell.TileType });
        saved.Sort((a, b) => a.q != b.q ? a.q.CompareTo(b.q) : a.r.CompareTo(b.r));
        return saved.ToArray();
    }

    // Load-only path: replaces the grid with the saved cells. Never charges cost, checks DEV or validates
    // affordability. Connections are refreshed once and GridRestored fires once at the end.
    public void RestoreState(GameSaveData.HexCellState[] saved)
    {
        EnsureGrid();
        foreach (EcoGridCell cell in cells.Values)
        {
            if (cell.Visual != null) Destroy(cell.Visual);
            cell.Clear();
        }
        if (saved != null)
            foreach (GameSaveData.HexCellState state in saved)
            {
                if (state == null || state.tileType == EnvironmentTileType.None) continue;
                var coord = new HexCoord(state.q, state.r);
                EcoGridCell cell = GetCell(coord);
                // A map resize must not break an older save; the stray cell is dropped with a warning.
                if (cell == null || !cell.IsEmpty)
                {
                    Debug.LogWarning($"{SaveId}: 저장된 Hex ({state.q},{state.r})를 복원할 수 없어 건너뜁니다.", this);
                    continue;
                }
                cell.SetTile(state.tileType, CreateTileVisual(state.tileType, coord));
            }
        RefreshTerrain();
        GridRestored?.Invoke();
    }

    private static EcoGridManager FindLiveGrid(string id)
    {
        foreach (EcoGridManager grid in liveGrids)
            if (grid != null && grid.SaveId == id) return grid;
        return null;
    }

    // Every grid state for the save file: live grids plus states still waiting for an unloaded scene.
    public static GameSaveData.HexGridState[] CaptureAll()
    {
        var states = new List<GameSaveData.HexGridState>();
        var captured = new HashSet<string>();
        foreach (EcoGridManager grid in liveGrids)
            if (grid != null && captured.Add(grid.SaveId))
                states.Add(new GameSaveData.HexGridState { id = grid.SaveId, cells = grid.CaptureState() });
        foreach (KeyValuePair<string, GameSaveData.HexCellState[]> pending in pendingStates)
            if (captured.Add(pending.Key))
                states.Add(new GameSaveData.HexGridState { id = pending.Key, cells = pending.Value });
        return states.ToArray();
    }

    // Load / new game. Null or missing entries (older saves, F8) leave grids empty.
    public static void RestoreAll(GameSaveData.HexGridState[] states)
    {
        pendingStates.Clear();
        var byId = new Dictionary<string, GameSaveData.HexCellState[]>();
        if (states != null)
            foreach (GameSaveData.HexGridState state in states)
                if (state != null && !string.IsNullOrWhiteSpace(state.id)) byId[state.id] = state.cells;
        var restored = new HashSet<string>();
        foreach (EcoGridManager grid in liveGrids)
        {
            if (grid == null || !restored.Add(grid.SaveId)) continue;
            byId.TryGetValue(grid.SaveId, out GameSaveData.HexCellState[] cellsForGrid);
            grid.RestoreState(cellsForGrid);
        }
        foreach (KeyValuePair<string, GameSaveData.HexCellState[]> entry in byId)
            if (!restored.Contains(entry.Key)) pendingStates[entry.Key] = entry.Value;
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

    // Refreshes view connections after placement, moving or removal.
    private void RefreshTerrain()
    {
        foreach (EcoGridCell cell in cells.Values)
        {
            if (cell.Visual == null) continue;
            IEcoTileView view = cell.Visual.GetComponent<IEcoTileView>();
            if (view == null) continue;
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
