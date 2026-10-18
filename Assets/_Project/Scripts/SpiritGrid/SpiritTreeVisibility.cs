using System;
using System.Collections.Generic;
using UnityEngine;

// Scene-local presentation only. Runs after the follow camera; no physics casts, colliders,
// cell data, or per-frame material instances are needed for these simple static trees.
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class SpiritTreeVisibility : MonoBehaviour
{
    [Serializable]
    private struct FadeMaterialPair
    {
        public Material opaque;
        public Material transparent;
    }

    [SerializeField] private Transform player;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Transform groundPlane;
    [SerializeField, Tooltip("Direct children of these groups are trees. Do not include rocks or bushes.")]
    private Transform[] treeGroups = new Transform[0];
    [SerializeField] private FadeMaterialPair[] fadeMaterials = new FadeMaterialPair[0];
    [SerializeField, Range(0.05f, 1f)] private float fadedAlpha = 0.35f;
    [SerializeField, Min(0f)] private float playerNearDistance = 4f;
    [SerializeField, Min(0f)] private float cameraNearDistance = 3f;
    [SerializeField, Min(0f), Tooltip("Width added around the camera sight lines, in world units.")]
    private float sightPadding = 1.5f;
    [SerializeField] private Vector3 playerViewOffset = new Vector3(0, 1, 0);
    [SerializeField, Min(0.01f)] private float checkInterval = 0.08f;
    [SerializeField, Min(0.1f)] private float fadeSpeed = 4f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly List<TreeState> trees = new List<TreeState>();
    private readonly Plane[] frustumPlanes = new Plane[6];
    private float nextCheck;

    private sealed class RendererState
    {
        public Renderer renderer;
        public Material originalMaterial, fadeMaterial;
        public Color originalColor;
        public MaterialPropertyBlock originalBlock, workingBlock;
    }

    private sealed class TreeState
    {
        public Transform root;
        public RendererState[] renderers;
        public float alpha = 1f;
        public bool obstructing;
    }

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        var seen = new HashSet<Transform>();
        foreach (Transform group in treeGroups)
        {
            if (group == null) continue;
            foreach (Transform tree in group)
            {
                if (!seen.Add(tree)) continue;
                var states = new List<RendererState>();
                foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>(true))
                {
                    Material original = renderer.sharedMaterial;
                    Material fade = FindFadeMaterial(original);
                    if (original == null || fade == null) continue;
                    var originalBlock = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(originalBlock);
                    Color color = originalBlock.HasColor(BaseColorId)
                        ? originalBlock.GetColor(BaseColorId) : original.GetColor(BaseColorId);
                    states.Add(new RendererState
                    {
                        renderer = renderer, originalMaterial = original, fadeMaterial = fade,
                        originalColor = color, originalBlock = originalBlock,
                        workingBlock = new MaterialPropertyBlock()
                    });
                }
                if (states.Count > 0) trees.Add(new TreeState { root = tree, renderers = states.ToArray() });
            }
        }
    }

    private Material FindFadeMaterial(Material original)
    {
        foreach (FadeMaterialPair pair in fadeMaterials)
            if (pair.opaque == original) return pair.transparent;
        return null;
    }

    private void OnEnable() { nextCheck = 0f; }

    private void LateUpdate()
    {
        if (viewCamera == null || player == null) { RestoreAll(); return; }
        if (Time.unscaledTime >= nextCheck)
        {
            CheckObstructions();
            nextCheck = Time.unscaledTime + Mathf.Max(0.01f, checkInterval);
        }
        foreach (TreeState tree in trees)
        {
            float target = tree.obstructing ? Mathf.Clamp01(fadedAlpha) : 1f;
            float alpha = Mathf.MoveTowards(tree.alpha, target, Mathf.Max(0.1f, fadeSpeed) * Time.unscaledDeltaTime);
            if (Mathf.Approximately(alpha, tree.alpha)) continue;
            tree.alpha = alpha;
            if (alpha >= 0.9999f) Restore(tree);
            else ApplyAlpha(tree, alpha);
        }
    }

    private void CheckObstructions()
    {
        GeometryUtility.CalculateFrustumPlanes(viewCamera, frustumPlanes);
        Vector3 cameraPosition = viewCamera.transform.position;
        Vector3 playerTarget = player.position + playerViewOffset;
        Vector3 playerViewport = viewCamera.WorldToViewportPoint(playerTarget);
        bool playerInView = playerViewport.z > 0 && playerViewport.x >= 0 && playerViewport.x <= 1
            && playerViewport.y >= 0 && playerViewport.y <= 1;
        Vector3 planePoint = groundPlane != null ? groundPlane.position : Vector3.zero;
        Vector3 planeNormal = groundPlane != null ? groundPlane.up : Vector3.up;
        var plane = new Plane(planeNormal, planePoint);
        Ray centreRay = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        bool hasCentre = plane.Raycast(centreRay, out float centreDistance);
        Vector3 centreTarget = hasCentre ? centreRay.GetPoint(centreDistance) : playerTarget;

        foreach (TreeState tree in trees)
        {
            tree.obstructing = false;
            if (tree.root == null || !tree.root.gameObject.activeInHierarchy || !TryBounds(tree, out Bounds bounds)) continue;
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) continue;
            tree.obstructing = IsNearPlayer(bounds, player.position, playerNearDistance)
                || bounds.SqrDistance(cameraPosition) <= cameraNearDistance * cameraNearDistance
                || (playerInView && BlocksSegment(bounds, cameraPosition, playerTarget, sightPadding))
                || (hasCentre && BlocksSegment(bounds, centreRay.origin, centreTarget, sightPadding));
        }
    }

    private static bool TryBounds(TreeState tree, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (RendererState state in tree.renderers)
        {
            Renderer renderer = state.renderer;
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (found) bounds.Encapsulate(renderer.bounds);
            else { bounds = renderer.bounds; found = true; }
        }
        return found;
    }

    // AABB padding approximates a short sight corridor without requiring tree colliders.
    internal static bool BlocksSegment(Bounds bounds, Vector3 from, Vector3 to, float padding)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.001f) return false;
        bounds.Expand(Mathf.Max(0, padding) * 2f);
        if (bounds.Contains(from)) return true;
        return bounds.IntersectRay(new Ray(from, delta / length), out float distance) && distance <= length;
    }

    internal static bool IsNearPlayer(Bounds bounds, Vector3 playerPosition, float distance)
    {
        Vector3 nearest = bounds.ClosestPoint(playerPosition);
        Vector2 offset = new Vector2(nearest.x - playerPosition.x, nearest.z - playerPosition.z);
        return offset.sqrMagnitude <= Mathf.Max(0, distance) * Mathf.Max(0, distance);
    }

    private static void ApplyAlpha(TreeState tree, float alpha)
    {
        foreach (RendererState state in tree.renderers)
        {
            if (state.renderer == null) continue;
            if (state.renderer.sharedMaterial != state.fadeMaterial) state.renderer.sharedMaterial = state.fadeMaterial;
            state.renderer.GetPropertyBlock(state.workingBlock);
            Color color = state.originalColor;
            color.a *= alpha;
            state.workingBlock.SetColor(BaseColorId, color);
            state.workingBlock.SetColor(ColorId, color);
            state.renderer.SetPropertyBlock(state.workingBlock);
        }
    }

    private static void Restore(TreeState tree)
    {
        foreach (RendererState state in tree.renderers)
        {
            if (state.renderer == null) continue;
            state.renderer.sharedMaterial = state.originalMaterial;
            state.renderer.SetPropertyBlock(state.originalBlock.isEmpty ? null : state.originalBlock);
        }
    }

    private void RestoreAll()
    {
        foreach (TreeState tree in trees)
        {
            if (tree.alpha < 1f) Restore(tree);
            tree.alpha = 1f;
            tree.obstructing = false;
        }
    }

    private void OnDisable() { RestoreAll(); }
    private void OnDestroy() { RestoreAll(); }
}
