using UnityEngine;

// One persistent Hex overlay over the usable forest ground.
[DisallowMultipleComponent]
public sealed class EcoBuildAreaView : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private Material overlayMaterial;
    [SerializeField] private Material gridLineMaterial;
    [SerializeField] private Color activeTint = new Color(0.54f, 0.733f, 0.38f, 1f);
    [SerializeField] private Color borderTint = new Color(0.92f, 1f, 0.82f, 0.95f);
    [SerializeField, Min(0.01f)] private float lineWidth = 0.14f;
    [SerializeField, Min(0.01f)] private float outerLineWidth = 0.22f;
    private GameObject overlayRoot;
    private Material gridLineInstance, fillInstance;
    private Texture2D cellState;
    public bool InputBlocked => false;
    public Material OverlayMaterial => overlayMaterial;

    private void Awake()
    {
        grid.EnsureGrid();
        overlayRoot = new GameObject("PersistentHexMap");
        overlayRoot.transform.SetParent(grid.GridRoot, false);
        RebuildMapOverlay();
    }

    // Rebuild the overlay from the forest cell set.
    public void RebuildMapOverlay()
    {
        if (gridLineMaterial == null || overlayRoot == null) return;
        foreach (Transform child in overlayRoot.transform)
        { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        if (gridLineInstance != null) Destroy(gridLineInstance);
        if (fillInstance != null) Destroy(fillInstance);
        if (cellState != null) Destroy(cellState);
        cellState = new Texture2D(grid.MapColumns, grid.MapRows, TextureFormat.RGBA32, false, true)
        { name = "HexMapState_Runtime", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[grid.MapColumns * grid.MapRows];
        foreach (HexCoord coord in grid.MapHexes)
            pixels[coord.r * grid.MapColumns + HexMetrics.OffsetColumn(coord)] =
                new Color(1, 1, 0, 1);
        cellState.SetPixels(pixels);
        cellState.Apply(false, false);
        Bounds bounds = grid.MapLocalBounds;
        var directions = new Vector4[HexCoord.Directions.Count];
        for (int d = 0; d < directions.Length; d++)
            directions[d] = new Vector4(HexCoord.Directions[d].q, HexCoord.Directions[d].r, 0, 0);
        gridLineInstance = new Material(gridLineMaterial) { name = "HexGridLines_Runtime" };
        gridLineInstance.SetFloat("_HexWidth", grid.CellSize);
        gridLineInstance.SetFloat("_LineWidth", lineWidth);
        gridLineInstance.SetFloat("_BorderWidth", outerLineWidth);
        gridLineInstance.SetFloat("_FillOnly", 0);
        gridLineInstance.SetVector("_MapMin", new Vector4(bounds.min.x, bounds.min.z, 0, 0));
        gridLineInstance.SetVector("_MapSize", new Vector4(bounds.size.x, bounds.size.z, 0, 0));
        gridLineInstance.SetVector("_MapDimensions", new Vector4(grid.MapColumns, grid.MapRows, 0, 0));
        gridLineInstance.SetVectorArray("_HexDirections", directions);
        gridLineInstance.SetTexture("_CellState", cellState);
        gridLineInstance.SetColor("_ActiveColor", activeTint);
        fillInstance = new Material(gridLineInstance) { name = "HexGroundTint_Runtime", renderQueue = 2002 };
        fillInstance.SetFloat("_FillOnly", 1);
        // Ground colour below all tile surfaces; elevated lines cannot z-fight with them.
        EcoVisualUtil.Surface(overlayRoot.transform, "HexGroundTint", bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z,
            0.006f, borderTint, fillInstance);
        EcoVisualUtil.Surface(overlayRoot.transform, "AlwaysVisibleHexLines", bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z,
            0.1f, borderTint, gridLineInstance);
    }

    public void SetVisible(bool visible)
    {
        // Both exploration and build mode keep the same grid readability.
        if (overlayRoot != null) overlayRoot.SetActive(true);
    }

    public Vector3 ClampCameraFocus(Vector3 focus)
    {
        Bounds bounds = grid.MapBounds;
        // ActiveBounds is contained in MapBounds. Do not subtract the viewport footprint,
        // so every map corner remains reachable at any zoom.
        bounds.Encapsulate(grid.ActiveBounds);
        focus.x = Mathf.Clamp(focus.x, bounds.min.x, bounds.max.x);
        focus.z = Mathf.Clamp(focus.z, bounds.min.z, bounds.max.z);
        return focus;
    }
    private void OnDestroy()
    {
        if (overlayRoot != null) Destroy(overlayRoot);
        if (gridLineInstance != null) Destroy(gridLineInstance);
        if (fillInstance != null) Destroy(fillInstance);
        if (cellState != null) Destroy(cellState);
    }
}
