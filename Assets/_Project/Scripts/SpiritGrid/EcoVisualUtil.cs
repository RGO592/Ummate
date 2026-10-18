using UnityEngine;

// Small helpers for runtime placeholder geometry (primitives tinted with property blocks).
public static class EcoVisualUtil
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static MaterialPropertyBlock block;

    public static void Paint(Renderer target, Color color)
    {
        if (target == null) return;
        block ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        target.SetPropertyBlock(block);
    }

    // Creates a coloured primitive under parent (local space) without a collider unless asked for.
    public static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 localPosition,
        Vector3 localScale, Color color, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
        }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        Paint(go.GetComponent<Renderer>(), color);
        return go;
    }

    // A zero-thickness plane; never a physical platform. Uses a shared material plus per-view tint.
    public static GameObject Surface(Transform parent, string name, float x0, float z0, float x1, float z1,
        float y, Color color, Material material)
    {
        GameObject go = Primitive(PrimitiveType.Quad, parent, name,
            new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f),
            new Vector3(x1 - x0, z1 - z0, 1f), color);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Renderer renderer = go.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    // Axis aligned box given by min / max corners in parent local space.
    public static GameObject Box(Transform parent, string name, Vector3 min, Vector3 max, Color color, bool keepCollider = false) =>
        Primitive(PrimitiveType.Cube, parent, name, (min + max) * 0.5f, max - min, color, keepCollider);
}
