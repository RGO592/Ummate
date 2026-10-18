using UnityEngine;
using UnityEngine.InputSystem;

// Presentation only: the entire ground exists independently of the unlocked cell data.
[DisallowMultipleComponent]
public sealed class EcoBuildAreaView : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private EcoExpansionPrompt prompt;
    [SerializeField] private Material overlayMaterial;
    [SerializeField] private Material gridLineMaterial;
    private Material gridLineInstance;
    [SerializeField, Tooltip("Centre of the continuous ground, independent of the active grid origin.")]
    private Transform groundCenter;
    [SerializeField] private Vector2 groundHalfExtents = new Vector2(108f, 108f);
    [SerializeField] private Color activeTint = new Color(0.42f, 0.85f, 0.46f, 0.09f);
    [SerializeField] private Color borderTint = new Color(0.92f, 1f, 0.82f, 0.95f);
    [SerializeField] private Color lockedTint = new Color(0.05f, 0.12f, 0.08f, 0.3f);
    [SerializeField, Min(0.01f)] private float lineWidth = 0.14f;
    [SerializeField, Min(0.01f)] private float outerLineWidth = 0.22f;
    private GameObject overlayRoot;
    private GameObject buildTintRoot;
    private bool buildModeVisible;
    public bool InputBlocked => prompt != null && prompt.InputBlocked;
    public Material OverlayMaterial => overlayMaterial;

    private void Awake()
    {
        overlayRoot = new GameObject("PersistentMapGrid");
        overlayRoot.transform.SetParent(grid.GridRoot, false);
        Vector2 h = grid.HalfExtents;
        Vector2 c = GroundLocalCenter;
        Vector2 min = c - groundHalfExtents, max = c + groundHalfExtents;

        // Locked land remains visible in both play and build modes, without physical platforms.
        Surface("LockedNorth", min.x, h.y, max.x, max.y, 0.04f, lockedTint);
        Surface("LockedSouth", min.x, min.y, max.x, -h.y, 0.04f, lockedTint);
        Surface("LockedWest", min.x, -h.y, -h.x, h.y, 0.04f, lockedTint);
        Surface("LockedEast", h.x, -h.y, max.x, h.y, 0.04f, lockedTint);

        // A single procedural line overlay avoids transparent strip sorting and subpixel gaps.
        // Elevation + depth bias separate it from the ground and tile surfaces.
        if (gridLineMaterial != null)
        {
            gridLineInstance = new Material(gridLineMaterial);
            gridLineInstance.SetFloat("_CellSize", grid.CellSize);
            gridLineInstance.SetFloat("_LineWidth", lineWidth);
            gridLineInstance.SetFloat("_BorderWidth", outerLineWidth);
            gridLineInstance.SetVector("_MapMin", new Vector4(min.x, min.y, 0, 0));
            gridLineInstance.SetVector("_MapSize", new Vector4(max.x - min.x, max.y - min.y, 0, 0));
            gridLineInstance.SetVector("_GridOrigin", new Vector4(-h.x, -h.y, 0, 0));
            gridLineInstance.SetVector("_ActiveRect", new Vector4(-h.x, -h.y, h.x, h.y));
            EcoVisualUtil.Surface(overlayRoot.transform, "ContinuousGridLines", min.x, min.y, max.x, max.y,
                0.1f, borderTint, gridLineInstance);
        }

        // Only the extra active-area tint follows build mode; lines and locked shade stay on.
        buildTintRoot = new GameObject("BuildAreaTint");
        buildTintRoot.transform.SetParent(overlayRoot.transform, false);
        EcoVisualUtil.Surface(buildTintRoot.transform, "ActiveArea", -h.x, -h.y, h.x, h.y,
            0.045f, activeTint, overlayMaterial);
        buildTintRoot.SetActive(buildModeVisible);
    }

    private Vector2 GroundLocalCenter
    {
        get
        {
            if (groundCenter == null) return Vector2.zero;
            Vector3 local = Quaternion.Inverse(grid.GridRoot.rotation) * (groundCenter.position - grid.GridRoot.position);
            return new Vector2(local.x, local.z);
        }
    }

    private void Surface(string label, float x0, float z0, float x1, float z1, float y, Color tint)
    {
        // At an exact map corner the south/west locked strips have zero area.
        if (x1 <= x0 || z1 <= z0) return;
        EcoVisualUtil.Surface(overlayRoot.transform, label, x0, z0, x1, z1, y, tint, overlayMaterial);
    }

    public void SetVisible(bool visible)
    {
        buildModeVisible = visible;
        if (buildTintRoot != null) buildTintRoot.SetActive(visible);
        // Kept for the build-mode controller API: the base map grid is always visible.
        if (overlayRoot != null) overlayRoot.SetActive(true);
        if (!visible && prompt != null) prompt.Close();
    }

    // Only the screen-centre target is bounded. Do not subtract the viewport footprint:
    // even the map corners and the active area's centre must be reachable at every zoom.
    public Vector3 ClampCameraFocus(Vector3 focus)
    {
        if (groundCenter == null) return focus;
        Vector3 center = groundCenter.position;
        focus.x = Mathf.Clamp(focus.x, center.x - groundHalfExtents.x, center.x + groundHalfExtents.x);
        focus.z = Mathf.Clamp(focus.z, center.z - groundHalfExtents.y, center.z + groundHalfExtents.y);
        return focus;
    }

    public bool IsLocked(Ray ray)
    {
        var plane = new Plane(grid.GridRoot.up, grid.GridRoot.position);
        if (!plane.Raycast(ray, out float distance)) return false;
        Vector3 world = ray.GetPoint(distance);
        Vector3 local = Quaternion.Inverse(grid.GridRoot.rotation) * (world - grid.GridRoot.position);
        Vector2 center = GroundLocalCenter;
        return Mathf.Abs(local.x - center.x) < groundHalfExtents.x && Mathf.Abs(local.z - center.y) < groundHalfExtents.y
            && !grid.WorldToGrid(world, out _);
    }

    public void ShowExpansionPrompt() { if (prompt != null) prompt.Open(); }

    public void HandleModalKeyboard()
    {
        if (prompt != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            prompt.Close();
    }

    private void OnDestroy()
    {
        if (overlayRoot != null) Destroy(overlayRoot);
        if (gridLineInstance != null) Destroy(gridLineInstance);
    }
}
