using UnityEngine;

// Used when TileSettings has no model prefab. Type-coloured surfaces keep object
// tiles readable; water retains its separately authored surface material.
// A supplied model prefab is instantiated directly by EcoGridManager instead of this view.
[DisallowMultipleComponent]
public sealed class EcoPlaceholderTileView : MonoBehaviour, IEcoTileView
{
    // Detail scale: layers and props grow slowly with the cell so big cells stay readable at human scale.
    private float detail = 1f;
    private const float SurfaceHeight = 0.025f;

    private EnvironmentTileType type;
    private float cellSize;
    private float groundInset;
    private Material waterMaterial;
    private Material surfaceMaterial;
    private HexCoord coord;
    private Transform layers;
    private Transform props;
    private HexDirectionMask mask = (HexDirectionMask)(-1);

    public void Build(EnvironmentTileType tileType, HexCoord cellCoord, float size, float edgeInset, Material material, Material tileSurfaceMaterial = null)
    {
        type = tileType;
        coord = cellCoord;
        cellSize = size;
        groundInset = Mathf.Clamp(edgeInset, 0f, size * 0.1f);
        // An explicit asset reference plus a bundled fallback keeps player builds portable.
        if (type == EnvironmentTileType.Water)
        {
            waterMaterial = material != null ? material : Resources.Load<Material>("SG_WaterSurface");
            if (waterMaterial == null)
                throw new System.InvalidOperationException("SpiritGrid requires Resources/SG_WaterSurface.mat and its .meta file.");
        }
        surfaceMaterial = tileSurfaceMaterial;
        detail = Mathf.Max(1f, Mathf.Sqrt(size / 2.5f));
        layers = new GameObject("Layers").transform;
        layers.SetParent(transform, false);
        props = new GameObject("Props").transform;
        props.SetParent(transform, false);
        BuildProps();
        SetNeighbors(HexDirectionMask.None);
    }

    // Called by the manager when the cell moves; props are re-seeded for the new cell.
    public void SetCoord(HexCoord cellCoord)
    {
        if (cellCoord == coord) return;
        coord = cellCoord;
        props.gameObject.SetActive(false);
        Destroy(props.gameObject);
        props = new GameObject("Props").transform;
        props.SetParent(transform, false);
        BuildProps();
    }

    public void SetNeighbors(HexDirectionMask newMask)
    {
        if (newMask == mask || layers == null) return;
        mask = newMask;
        // Replace the whole container: Destroy is deferred, so reusing it would mix old and new pieces.
        layers.gameObject.SetActive(false);
        Destroy(layers.gameObject);
        layers = new GameObject("Layers").transform;
        layers.SetParent(transform, false);
        if (type == EnvironmentTileType.Water) BuildWaterSurface();
        else BuildTypeSurface();
    }

    private void BuildTypeSurface()
    {
        Color color = EnvironmentTileTypes.DefaultColor(type);
        // SG_TileSurface renders after HexGroundTint (2002), before grid lines (3010).
        // The ground fill uses depth bias, so drawing it after this face caused a
        // camera-dependent horizontal cutoff even with different surface heights.
        HexMeshUtil.Create(layers, "ConnectedTypeSurface",
            HexMeshUtil.InsetSurface(cellSize, groundInset, mask), SurfaceHeight,
            color, surfaceMaterial);
    }

    // Only exposed water edges retain an inset. Joined faces have no rim or gap.
    private void BuildWaterSurface()
    {
        // Preserve the authored water colours instead of overriding them with DefaultColor(Water).
        HexMeshUtil.Create(layers, "ConnectedWaterSurface",
            HexMeshUtil.InsetSurface(cellSize, groundInset, mask), SurfaceHeight,
            Color.white, waterMaterial, tintMaterial: false);
    }

    // Props keep a near-human scale (grows with sqrt of the cell) and get denser as cells get bigger.
    private void BuildProps()
    {
        float k = cellSize / 2.5f;              // area factor per side
        float s = detail;                        // prop size factor
        float spread = cellSize * 0.38f;         // stay inside the rim
        var random = new System.Random(coord.q * 73856093 ^ coord.r * 19349663 ^ (int)type * 83492791);
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector3 RandomSpot(float y)
        {
            Vector3 p;
            do { p = new Vector3(Range(-spread, spread), 0, Range(-spread, spread)); }
            while (!HexMetrics.Contains(p, cellSize, cellSize * 0.15f));
            p.y = y;
            return p;
        }
        float top = SurfaceHeight;
        switch (type)
        {
            case EnvironmentTileType.Wheat:
            {
                int rows = Mathf.Max(3, Mathf.RoundToInt(3f * Mathf.Sqrt(k)));
                float step = spread * 2f / (rows - 1);
                Vector3 size = new Vector3(0.14f, 0.7f, 0.14f) * s;
                for (int x = 0; x < rows; x++)
                    for (int z = 0; z < rows; z++)
                    {
                        Vector3 spot = new Vector3(-spread + x * step + Range(-0.1f, 0.1f) * s,
                            top + size.y * 0.5f, -spread + z * step + Range(-0.1f, 0.1f) * s);
                        if (HexMetrics.Contains(spot, cellSize, groundInset + size.x))
                            EcoVisualUtil.Primitive(PrimitiveType.Cube, props, "Stalk", spot, size, new Color(0.88f, 0.7f, 0.22f));
                    }
                break;
            }
            case EnvironmentTileType.Flower:
            {
                Color[] petals = { Color.white, new Color(1f, 0.9f, 0.3f), new Color(0.95f, 0.3f, 0.4f), new Color(0.7f, 0.45f, 0.95f) };
                int count = Mathf.RoundToInt(6f * k);
                for (int i = 0; i < count; i++)
                    EcoVisualUtil.Primitive(PrimitiveType.Sphere, props, "Blossom",
                        RandomSpot(top + 0.15f * s), Vector3.one * 0.3f * s, petals[random.Next(petals.Length)]);
                break;
            }
            case EnvironmentTileType.Water:
            {
                int count = Mathf.RoundToInt(k);
                for (int i = 0; i < count; i++)
                    if (random.NextDouble() < 0.6)
                        EcoVisualUtil.Primitive(PrimitiveType.Cylinder, props, "LilyPad",
                            RandomSpot(SurfaceHeight + 0.025f), new Vector3(0.8f, 0.01f, 0.8f) * s, new Color(0.3f, 0.65f, 0.3f));
                break;
            }
            case EnvironmentTileType.Rock:
            {
                int count = 1 + Mathf.RoundToInt(k * 0.5f);
                for (int i = 0; i < count; i++)
                {
                    float scale = Range(0.6f, 1.2f) * s;
                    Vector3 size = new Vector3(1.0f, 0.7f, 0.85f) * scale;
                    GameObject boulder = EcoVisualUtil.Primitive(PrimitiveType.Cube, props, "Boulder",
                        RandomSpot(top + size.y * 0.45f), size, new Color(0.5f, 0.5f, 0.53f) * Range(0.9f, 1.1f));
                    boulder.transform.localRotation = Quaternion.Euler(Range(-8f, 8f), random.Next(0, 90), Range(-8f, 8f));
                }
                break;
            }
            case EnvironmentTileType.Tree:
            {
                // One tree per quadrant slot, a few slots per cell, so canopies rarely overlap.
                Vector2[] slots = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) };
                int count = Mathf.Clamp(Mathf.RoundToInt(k * 0.75f), 1, slots.Length);
                int skip = random.Next(slots.Length);
                for (int i = 0; i < count; i++)
                {
                    Vector2 slot = slots[(skip + i) % slots.Length] * spread * 0.5f;
                    float scale = Range(0.85f, 1.15f) * s * 1.3f;
                    Vector3 trunkPos = new Vector3(slot.x + Range(-0.3f, 0.3f) * s, top + 0.6f * scale, slot.y + Range(-0.3f, 0.3f) * s);
                    EcoVisualUtil.Primitive(PrimitiveType.Cylinder, props, "Trunk", trunkPos,
                        new Vector3(0.3f, 0.6f, 0.3f) * scale, new Color(0.45f, 0.3f, 0.18f));
                    EcoVisualUtil.Primitive(PrimitiveType.Sphere, props, "Canopy",
                        trunkPos + new Vector3(0f, 1.25f * scale, 0f), new Vector3(1.8f, 1.6f, 1.8f) * scale, new Color(0.22f, 0.5f, 0.24f));
                }
                break;
            }
        }
    }
}
