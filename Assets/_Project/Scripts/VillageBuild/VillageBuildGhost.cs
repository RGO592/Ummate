using UnityEngine;

// Placement preview: a footprint tile plus a mesh-only copy of the facility prefab. It copies no
// components or colliders, so it can never be interacted with or block the placement check.
[DisallowMultipleComponent]
public sealed class VillageBuildGhost : MonoBehaviour
{
    [SerializeField] private Material ghostMaterial;
    [SerializeField] private Color placeableColor = new Color(0.25f, 0.9f, 0.4f, 0.5f);
    [SerializeField] private Color blockedColor = new Color(0.95f, 0.2f, 0.15f, 0.5f);
    [SerializeField, Tooltip("Footprint tile height above the ground, over the build grid.")]
    private float tileLift = 0.05f;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private GameObject root;
    private MeshRenderer[] renderers = new MeshRenderer[0];
    private MaterialPropertyBlock colors;

    public void Show(VillageFacilityDefinition facility)
    {
        Hide();
        root = new GameObject($"Ghost_{facility.Id}");
        root.transform.SetParent(transform, false);

        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Vector2Int size = facility.Footprint;
        AddPart("Footprint", cube, new Vector3(0f, tileLift, 0f), Quaternion.identity, new Vector3(size.x, 0.02f, size.y));

        if (facility.Prefab != null)
        {
            Transform source = facility.Prefab.transform;
            Matrix4x4 toRoot = source.worldToLocalMatrix;
            foreach (MeshFilter filter in facility.Prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Transform part = filter.transform;
                AddPart(part.name, filter.sharedMesh, toRoot.MultiplyPoint3x4(part.position),
                    Quaternion.Inverse(source.rotation) * part.rotation, part.lossyScale);
            }
        }
        renderers = root.GetComponentsInChildren<MeshRenderer>();
    }

    private void AddPart(string partName, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var part = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(root.transform, false);
        part.transform.SetLocalPositionAndRotation(position, rotation);
        part.transform.localScale = scale;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer view = part.GetComponent<MeshRenderer>();
        view.sharedMaterial = ghostMaterial;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
    }

    // The footprint tile and model are children of the root, so they turn together.
    public void MoveTo(Vector3 position, Quaternion rotation, bool placeable)
    {
        if (root == null) return;
        root.transform.SetPositionAndRotation(position, rotation);
        if (colors == null) colors = new MaterialPropertyBlock();
        colors.SetColor(BaseColor, placeable ? placeableColor : blockedColor);
        foreach (MeshRenderer view in renderers) view.SetPropertyBlock(colors);
    }

    public void Hide()
    {
        if (root != null) Destroy(root);
        root = null;
        renderers = new MeshRenderer[0];
    }
}
