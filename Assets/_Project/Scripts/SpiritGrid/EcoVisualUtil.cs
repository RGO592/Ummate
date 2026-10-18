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
        if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        Paint(go.GetComponent<Renderer>(), color);
        return go;
    }

    // Axis aligned box given by min / max corners in parent local space.
    public static GameObject Box(Transform parent, string name, Vector3 min, Vector3 max, Color color, bool keepCollider = false) =>
        Primitive(PrimitiveType.Cube, parent, name, (min + max) * 0.5f, max - min, color, keepCollider);
}
