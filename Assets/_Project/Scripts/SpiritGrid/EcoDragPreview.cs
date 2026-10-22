using System;
using System.Collections.Generic;
using UnityEngine;

// Presentation only. No tile component, collider, inventory entry, or saved cell is created.
// A screen overlay carries the item above UI; the same type's world ghost snaps on the ground.
public sealed class EcoDragPreview : IDisposable
{
    private readonly GameObject worldRoot;
    private readonly GameObject cursorCanvas;
    private readonly RectTransform cursorVisual;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> colors = new List<Color>();
    private readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();
    private readonly Material material;
    private readonly Color typeColor;
    private bool disposed;

    public EcoDragPreview(Transform parent, EnvironmentTileType type, float cellSize, Material overlay)
    {
        typeColor = EnvironmentTileTypes.DefaultColor(type);
        material = overlay;
        worldRoot = new GameObject("DragPreviewRoot");
        worldRoot.transform.SetParent(parent, false);
        var visual = new GameObject("PreviewVisual").transform;
        visual.SetParent(worldRoot.transform, false);
        BuildWorldVisual(visual, type, cellSize);

        cursorCanvas = new GameObject("DragPreviewCursor", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        Canvas canvas = cursorCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        CanvasGroup group = cursorCanvas.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        // No GraphicRaycaster: the ghost never counts as UI for placement validation.
        cursorVisual = new GameObject("PreviewVisual", typeof(RectTransform)).GetComponent<RectTransform>();
        cursorVisual.SetParent(cursorCanvas.transform, false);
        cursorVisual.anchorMin = cursorVisual.anchorMax = new Vector2(0.5f, 0.5f);
        cursorVisual.sizeDelta = new Vector2(72, 72);
        BuildCursorVisual(type);
        // Resolve the new overlay's screen rect before the immediate PointerDown preview.
        Canvas.ForceUpdateCanvases();
        worldRoot.SetActive(false);
        cursorCanvas.SetActive(false);
    }

    public void Show(Vector2 screen, bool useWorld, Vector3 world, Quaternion rotation, bool valid)
    {
        if (disposed) return;
        // Both representations switch in the same call; neither relies on a grid hit to exist.
        worldRoot.SetActive(useWorld);
        cursorCanvas.SetActive(!useWorld);
        if (useWorld)
        {
            worldRoot.transform.SetPositionAndRotation(world, rotation);
            for (int i = 0; i < renderers.Count; i++)
            {
                Color color = valid ? colors[i] : Color.Lerp(colors[i], new Color(1f, 0.12f, 0.1f), 0.75f);
                color.a = 0.65f;
                EcoVisualUtil.Paint(renderers[i], color);
            }
        }
        else
        {
            RectTransform canvasRect = (RectTransform)cursorCanvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            cursorVisual.anchoredPosition = local;
            // Readable across Game view sizes, independent of the build bar's CanvasScaler.
            cursorVisual.localScale = Vector3.one * Mathf.Clamp(Screen.height / 720f, 0.65f, 2f);
        }
    }

    private void BuildWorldVisual(Transform parent, EnvironmentTileType type, float size)
    {
        GameObject footprint = HexMeshUtil.Create(parent, "HexFootprint",
            HexMeshUtil.InsetSurface(size, size * 0.02f, HexDirectionMask.None), 0.12f, typeColor, material);
        renderers.Add(footprint.GetComponent<Renderer>());
        colors.Add(typeColor);
        float k = size / 10f;
        if (type == EnvironmentTileType.Water)
        {
            for (int i = -1; i <= 1; i++)
                Add(parent, PrimitiveType.Cube, "Ripple", new Vector3(i * k, 0.17f, i * 2 * k),
                    new Vector3(4 * k, 0.035f, 0.14f * k), new Color(0.65f, 0.88f, 1f));
        }
        else if (type == EnvironmentTileType.Wheat)
        {
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    Add(parent, PrimitiveType.Cube, "Wheat", new Vector3(x * 2.6f * k, 0.8f * k, z * 2.6f * k),
                        new Vector3(0.3f, 1.5f, 0.3f) * k, new Color(1f, 0.85f, 0.35f));
        }
        else if (type == EnvironmentTileType.Flower)
        {
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    Add(parent, PrimitiveType.Sphere, "Blossom", new Vector3(x * 2.7f * k, 0.5f * k, z * 2.7f * k),
                        Vector3.one * 0.9f * k, (x + z) % 2 == 0 ? Color.white : new Color(1, 0.4f, 0.6f));
        }
        else if (type == EnvironmentTileType.Rock)
        {
            for (int i = -1; i <= 1; i++)
                Add(parent, PrimitiveType.Cube, "Boulder", new Vector3(i * 2.5f * k, k, i * 1.3f * k),
                    new Vector3(2.3f, 2, 1.8f) * k, new Color(0.55f, 0.57f, 0.61f), Quaternion.Euler(5, i * 25, 8));
        }
        else if (type == EnvironmentTileType.Tree)
        {
            Add(parent, PrimitiveType.Cube, "Trunk", new Vector3(0, 1.5f * k, 0),
                new Vector3(0.6f, 3, 0.6f) * k, new Color(0.45f, 0.3f, 0.18f));
            Add(parent, PrimitiveType.Sphere, "Canopy", new Vector3(0, 3.7f * k, 0),
                new Vector3(4.2f, 3.7f, 4.2f) * k, new Color(0.25f, 0.65f, 0.3f));
        }
    }

    private void Add(Transform parent, PrimitiveType kind, string name, Vector3 position, Vector3 scale, Color color,
        Quaternion? rotation = null)
    {
        if (!meshes.TryGetValue(kind, out Mesh mesh))
        {
            // Borrow Unity's shared built-in mesh, then discard the inactive template.
            // The actual ghost hierarchy contains only Transform, MeshFilter and MeshRenderer.
            GameObject template = GameObject.CreatePrimitive(kind);
            template.SetActive(false);
            mesh = template.GetComponent<MeshFilter>().sharedMesh;
            meshes.Add(kind, mesh);
            UnityEngine.Object.Destroy(template);
        }
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        color.a = 0.65f;
        EcoVisualUtil.Paint(renderer, color);
        renderers.Add(renderer);
        colors.Add(color);
    }

    private void BuildCursorVisual(EnvironmentTileType type)
    {
        Stamp("InvalidDropBorder", Vector2.zero, new Vector2(74,74), new Color(1,0.25f,0.18f,0.9f));
        Color fill = typeColor; fill.a = 0.9f;
        Stamp("Environment", Vector2.zero, new Vector2(68,68), fill);
        // Type-specific marks make the carried object readable even while above an opaque UI panel.
        if (type == EnvironmentTileType.Water)
            for (int i = -1; i <= 1; i++) Stamp("Ripple", new Vector2(i*4,i*14), new Vector2(38,3), Color.white);
        else if (type == EnvironmentTileType.Wheat)
            for (int i = -1; i <= 1; i++) Stamp("Wheat", new Vector2(i*15,0), new Vector2(5,36), new Color(1,0.95f,0.5f));
        else if (type == EnvironmentTileType.Flower)
        {
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI / 2;
                Stamp("Petal", new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*13, new Vector2(16,16), Color.white,45);
            }
            Stamp("Centre", Vector2.zero, new Vector2(13,13), new Color(1,0.9f,0.2f));
        }
        else if (type == EnvironmentTileType.Rock)
        {
            Stamp("Boulder", new Vector2(-9,-5), new Vector2(27,25), new Color(0.38f,0.4f,0.45f),18);
            Stamp("Boulder", new Vector2(13,11), new Vector2(21,21), new Color(0.8f,0.82f,0.85f),-12);
        }
        else if (type == EnvironmentTileType.Tree)
        {
            Stamp("Trunk", new Vector2(0,-13), new Vector2(8,28), new Color(0.5f,0.3f,0.15f));
            Stamp("Canopy", new Vector2(0,9), new Vector2(32,32), new Color(0.14f,0.4f,0.16f),45);
        }
    }

    private void Stamp(string name, Vector2 position, Vector2 size, Color color, float angle = 0)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(cursorVisual, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f,0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.Euler(0,0,angle);
        UnityEngine.UI.Image image = go.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        image.color = color;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        worldRoot.SetActive(false);
        cursorCanvas.SetActive(false);
        UnityEngine.Object.Destroy(worldRoot);
        UnityEngine.Object.Destroy(cursorCanvas);
    }
}
