using UnityEngine;

// The living-area rectangle the player may build in, on a 1 m square grid in world X/Z.
// Answers the static half of the placement check and draws the grid shown only in build mode.
[DisallowMultipleComponent]
public sealed class VillageBuildArea : MonoBehaviour
{
    [Header("Area (world cells, 1 cell = 1 m)")]
    [SerializeField] private Vector2Int minCell = new Vector2Int(-16, -16);
    [SerializeField] private Vector2Int sizeInCells = new Vector2Int(28, 20);
    [SerializeField] private float groundHeight;

    [Header("Blocked")]
    [SerializeField, Tooltip("Surfaces without colliders that must stay clear: roads, the flight pad.")]
    private Renderer[] reservedSurfaces = new Renderer[0];
    [SerializeField, Tooltip("Height checked for colliders above the ground. Buildings, fences, triggers and the player inside it block a cell.")]
    private float clearanceHeight = 3f;

    [Header("Grid view")]
    [SerializeField, Tooltip("Existing 1 m grid object over this area. Off except in build mode.")]
    private GameObject gridOverlay;

    private const float CellInset = 0.02f;
    private const float GroundClearance = 0.05f;
    private Collider[] overlaps = new Collider[32];

    public float GroundHeight => groundHeight;
    public RectInt Cells => new RectInt(minCell.x, minCell.y, sizeInCells.x, sizeInCells.y);

    public bool TryGroundPoint(Ray ray, out Vector3 point)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
        bool hit = plane.Raycast(ray, out float enter);
        point = hit ? ray.GetPoint(enter) : default;
        return hit;
    }

    // The footprint centred on the cursor, snapped so even-sized footprints sit on grid lines.
    public RectInt FootprintAt(Vector3 world, Vector2Int size) => new RectInt(
        Mathf.RoundToInt(world.x - size.x * 0.5f), Mathf.RoundToInt(world.z - size.y * 0.5f), size.x, size.y);

    public Vector3 FootprintCenter(RectInt cells) => new Vector3(cells.center.x, groundHeight, cells.center.y);

    public static Vector2Int CellAt(Vector3 world) => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));

    // Returns why the cells are blocked, or null when clear. Placed facilities are tracked by the
    // controller's cell list, so their colliders (a tree's is wider than its cell) are ignored here.
    public string FindStaticBlocker(RectInt cells)
    {
        RectInt area = Cells;
        if (cells.xMin < area.xMin || cells.yMin < area.yMin || cells.xMax > area.xMax || cells.yMax > area.yMax)
            return "건설 구역 밖입니다.";
        foreach (Renderer surface in reservedSurfaces)
            if (surface != null && Overlaps(cells, surface.bounds)) return "길과 정거장에는 지을 수 없습니다.";

        Vector3 center = FootprintCenter(cells) + Vector3.up * (GroundClearance + clearanceHeight * 0.5f);
        var half = new Vector3(cells.width * 0.5f - CellInset, clearanceHeight * 0.5f, cells.height * 0.5f - CellInset);
        int count;
        while ((count = Physics.OverlapBoxNonAlloc(center, half, overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Collide)) == overlaps.Length)
            overlaps = new Collider[overlaps.Length * 2];
        for (int i = 0; i < count; i++)
        {
            Collider hit = overlaps[i];
            if (hit.GetComponentInParent<VillagePlacedFacility>() != null) continue;
            if (hit.GetComponentInParent<PlayerMovement>() != null) return "플레이어가 서 있는 자리입니다.";
            return hit.isTrigger ? "통행 구역에는 지을 수 없습니다." : "다른 물체와 겹칩니다.";
        }
        return null;
    }

    private static bool Overlaps(RectInt cells, Bounds bounds) =>
        cells.xMin + CellInset < bounds.max.x && cells.xMax - CellInset > bounds.min.x &&
        cells.yMin + CellInset < bounds.max.z && cells.yMax - CellInset > bounds.min.z;

    public void SetGridVisible(bool visible)
    {
        if (gridOverlay != null) gridOverlay.SetActive(visible);
    }
}
