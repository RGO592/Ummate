using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Visual scenery only. Never creates EcoGridCell, tile views, colliders or placement targets.
// The scene places this component under GridRoot with an identity local transform.
[DisallowMultipleComponent]
public sealed class ForestBackgroundHexView : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private Material backgroundMaterial;
    [SerializeField, Min(0f), Tooltip("Padding in GridRoot units. 310 covers distance 120 / FOV 60 / pitch 80, through 32:9, including all pan corners and orbit angles.")]
    private float backgroundPadding = 310f;
    [SerializeField] private float surfaceHeight = 0.006f;
    private GameObject visual;
    public int BackgroundHexCount { get; private set; }

    private void Awake()
    {
        if (grid == null || backgroundMaterial == null)
        {
            Debug.LogError("Background Hex requires its grid reference and included background material.", this);
            return;
        }
        grid.EnsureGrid();
        var gameplay = new HashSet<HexCoord>(grid.CellCoordinates);
        if (gameplay.Count == 0) return;
        int minCol = int.MaxValue, minRow = int.MaxValue, maxCol = int.MinValue, maxRow = int.MinValue;
        foreach (HexCoord coord in gameplay)
        {
            int col = HexMetrics.OffsetColumn(coord);
            minCol = Mathf.Min(minCol, col); maxCol = Mathf.Max(maxCol, col);
            minRow = Mathf.Min(minRow, coord.r); maxRow = Mathf.Max(maxRow, coord.r);
        }
        float width = grid.CellSize;
        int padColumns = Mathf.CeilToInt(backgroundPadding / width);
        int padRows = Mathf.CeilToInt(backgroundPadding / (HexMetrics.Radius(width) * 1.5f));
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        for (int row = minRow - padRows; row <= maxRow + padRows; row++)
            for (int col = minCol - padColumns; col <= maxCol + padColumns; col++)
            {
                HexCoord coord = HexMetrics.FromOffset(col, row);
                if (gameplay.Contains(coord)) continue; // An exact hole, not a second surface under the working grid.
                Vector3 center = HexMetrics.LocalCenter(coord, width);
                uint hash = unchecked((uint)(col * 73856093) ^ (uint)(row * 19349663));
                hash ^= hash >> 13; hash = unchecked(hash * 1274126177u); hash ^= hash >> 16;
                var variation = new Color((hash & 65535u) / 65535f, 0f, 0f, 1f);
                int start = vertices.Count;
                vertices.Add(center); uv.Add(Vector2.zero); colors.Add(variation);
                for (int corner = 0; corner < 6; corner++)
                {
                    Vector3 offset = HexMetrics.Corner(corner, width);
                    vertices.Add(center + offset);
                    uv.Add(new Vector2(offset.x, offset.z)); colors.Add(variation);
                    triangles.Add(start);
                    triangles.Add(start + (corner + 1) % 6 + 1);
                    triangles.Add(start + corner + 1);
                }
                BackgroundHexCount++;
            }
        var mesh = new Mesh
        {
            name = "ForestBackgroundHex_VisualOnly",
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        visual = HexMeshUtil.Create(transform, "VisualOnlySurface", mesh, surfaceHeight,
            Color.white, backgroundMaterial, false);
        visual.layer = 2; // Ignore Raycast; no Collider is created in the first place.
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_HexWidth", width);
        visual.GetComponent<MeshRenderer>().SetPropertyBlock(properties);
    }

    private void OnDestroy()
    {
        if (visual != null) Destroy(visual); // HexGeneratedMesh releases only our generated mesh.
    }
}
