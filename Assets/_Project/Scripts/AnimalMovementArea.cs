using UnityEngine;

// Defines a rectangle on this Transform's local XZ plane. No movement or physics logic.
[DisallowMultipleComponent]
public sealed class AnimalMovementArea : MonoBehaviour
{
    [SerializeField] private Vector2 size = new Vector2(5f, 5f);
    public Vector2 Size => size;

    // World-axis bounds for the axis-aligned pens in FarmScene.
    // For a rotated pen, use Transform.TransformPoint with the local Size rectangle instead.
    public Bounds WorldBounds
    {
        get
        {
            Vector3 half = new Vector3(size.x, 0f, size.y) * 0.5f;
            Bounds bounds = new Bounds(transform.TransformPoint(-half), Vector3.zero);
            bounds.Encapsulate(transform.TransformPoint(half));
            bounds.Encapsulate(transform.TransformPoint(new Vector3(half.x, 0f, -half.z)));
            bounds.Encapsulate(transform.TransformPoint(new Vector3(-half.x, 0f, half.z)));
            return bounds;
        }
    }

    private void OnValidate()
    {
        size.x = Mathf.Max(0.1f, size.x);
        size.y = Mathf.Max(0.1f, size.y);
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.02f, size.y));
        Gizmos.matrix = previous;
        Gizmos.color = previousColor;
    }
}
