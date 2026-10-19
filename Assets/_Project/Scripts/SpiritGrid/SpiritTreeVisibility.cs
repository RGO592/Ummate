using System;
using System.Collections.Generic;
using UnityEngine;

// One manager supplies a player-centred circular shader mask to nearby trees.
// Only pixels inside the circle fade; physics and the rest of each tree stay unchanged.
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
    [SerializeField, Tooltip("Direct children of these groups are trees. Do not include rocks or bushes.")]
    private Transform[] treeGroups = new Transform[0];
    [SerializeField] private FadeMaterialPair[] fadeMaterials = new FadeMaterialPair[0];
    [SerializeField, Min(0f), Tooltip("Radius in the camera-facing plane through the player. Only pixels inside the circle fade.")]
    private float fadeRadius = 10f;
    [SerializeField, Range(0f, 0.9f), Tooltip("Fraction of the radius kept at Fade Alpha before the outer gradient starts.")]
    private float fadeInnerRatio = 0.6f;
    [SerializeField] private Vector3 fadeCenterOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Range(0.05f, 1f)] private float fadeAlpha = 0.2f;
    [SerializeField, Min(0.1f)] private float fadeSpeed = 5f;
    [SerializeField, Min(0.01f)] private float checkInterval = 0.08f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int FadeCenterId = Shader.PropertyToID("_TreeFadeCenter");
    private static readonly int FadeRadiusId = Shader.PropertyToID("_TreeFadeRadius");
    private static readonly int FadeInnerRatioId = Shader.PropertyToID("_TreeFadeInnerRatio");
    private static readonly int FadeAlphaId = Shader.PropertyToID("_TreeFadeAlpha");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly List<TreeState> trees = new List<TreeState>();
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
        public bool nearFadeZone;
    }

    private void Awake()
    {
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
        if (player == null) { RestoreAll(); return; }
        if (Time.unscaledTime >= nextCheck)
        {
            CheckFadeZone();
            nextCheck = Time.unscaledTime + Mathf.Max(0.01f, checkInterval);
        }
        foreach (TreeState tree in trees)
        {
            float target = tree.nearFadeZone ? Mathf.Clamp01(fadeAlpha) : 1f;
            float alpha = Mathf.MoveTowards(tree.alpha, target, Mathf.Max(0.1f, fadeSpeed) * Time.unscaledDeltaTime);
            if (alpha >= 0.9999f)
            {
                if (tree.alpha < 1f) Restore(tree);
            }
            else ApplyMask(tree, alpha); // Centre must follow every frame, even at stable alpha.
            tree.alpha = alpha;
        }
    }

    private void CheckFadeZone()
    {
        Vector3 centre = player.position + fadeCenterOffset;
        // Broad phase only: include intersecting canopies even when their trunk is outside.
        // The shader, not a tree-level distance test, defines the visible circular edge.
        float nearbyDistance = Mathf.Max(0f, fadeRadius) * 2f;
        foreach (TreeState tree in trees)
        {
            tree.nearFadeZone = false;
            if (tree.root == null || !tree.root.gameObject.activeInHierarchy) continue;
            foreach (RendererState state in tree.renderers)
            {
                if (state.renderer != null && state.renderer.enabled
                    && state.renderer.bounds.SqrDistance(centre) <= nearbyDistance * nearbyDistance)
                {
                    tree.nearFadeZone = true;
                    break;
                }
            }
        }
    }

    private void ApplyMask(TreeState tree, float alpha)
    {
        Vector3 centre = player.position + fadeCenterOffset;
        foreach (RendererState state in tree.renderers)
        {
            if (state.renderer == null) continue;
            if (state.renderer.sharedMaterial != state.fadeMaterial) state.renderer.sharedMaterial = state.fadeMaterial;
            state.renderer.GetPropertyBlock(state.workingBlock);
            // Never reduce the whole renderer's base alpha.
            state.workingBlock.SetColor(BaseColorId, state.originalColor);
            state.workingBlock.SetColor(ColorId, state.originalColor);
            state.workingBlock.SetVector(FadeCenterId, new Vector4(centre.x, centre.y, centre.z, 1f));
            state.workingBlock.SetFloat(FadeRadiusId, Mathf.Max(0.001f, fadeRadius));
            state.workingBlock.SetFloat(FadeAlphaId, alpha);
            state.workingBlock.SetFloat(FadeInnerRatioId, Mathf.Clamp(fadeInnerRatio, 0f, 0.9f));
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
            tree.nearFadeZone = false;
        }
    }

    private void OnDisable() { RestoreAll(); }
    private void OnDestroy() { RestoreAll(); }
}
