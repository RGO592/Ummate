using UnityEngine;

// Flat geometry only. No collider, tile state, material instances or saved objects.
public static class HexMeshUtil
{
    public static Vector3[] InsetVertices(float width, float inset, HexDirectionMask neighbors)
    {
        var vertices = new Vector3[6];
        for (int d = 0; d < 6; d++)
        {
            int next = (d + 1) % 6;
            Vector3 a = HexMetrics.EdgeNormal(d), b = HexMetrics.EdgeNormal(next);
            float da = width * 0.5f - (((int)neighbors & (1 << d)) != 0 ? 0f : inset);
            float db = width * 0.5f - (((int)neighbors & (1 << next)) != 0 ? 0f : inset);
            float determinant = a.x * b.z - b.x * a.z;
            vertices[d] = new Vector3((da * b.z - db * a.z) / determinant, 0,
                (a.x * db - b.x * da) / determinant);
        }
        return vertices;
    }
    public static Mesh Face(float width) => InsetSurface(width, 0f, HexDirectionMask.All);
    public static Mesh InsetSurface(float width, float inset, HexDirectionMask neighbors)
    {
        Vector3[] corners = InsetVertices(width, inset, neighbors);
        var vertices = new Vector3[7];
        var triangles = new int[18];
        for (int d = 0; d < 6; d++)
        {
            vertices[d + 1] = corners[d];
            // Reverse XZ counter-clockwise winding so the face normal is +Y.
            triangles[d * 3] = 0;
            triangles[d * 3 + 1] = (d + 1) % 6 + 1;
            triangles[d * 3 + 2] = d + 1;
        }
        return Mesh("HexSurface", vertices, triangles);
    }
    public static Mesh Outline(float width, float thickness)
    {
        var vertices = new Vector3[12];
        var triangles = new int[36];
        Vector3[] inner = InsetVertices(width, thickness, HexDirectionMask.None);
        for (int d = 0; d < 6; d++)
        {
            vertices[d] = HexMetrics.Corner(d, width);
            vertices[d + 6] = inner[d];
            int next = (d + 1) % 6, i = d * 6;
            triangles[i] = d; triangles[i+1] = d+6; triangles[i+2] = next;
            triangles[i+3] = next; triangles[i+4] = d+6; triangles[i+5] = next+6;
        }
        return Mesh("HexOutline", vertices, triangles);
    }
    private static Mesh Mesh(string name, Vector3[] vertices, int[] triangles)
    {
        var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
    public static GameObject Create(Transform parent, string name, Mesh mesh, float y, Color color, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(HexGeneratedMesh));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * y;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<HexGeneratedMesh>().OwnedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material != null ? material : Resources.Load<Material>("SG_RuntimePrimitiveLit");
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        EcoVisualUtil.Paint(renderer, color);
        return go;
    }
}
