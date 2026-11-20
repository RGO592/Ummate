using UnityEngine;

// A visual resident of the forest. Discovery persists when the player rearranges the Hexes.
public sealed class HabitatAnimalView : MonoBehaviour
{
    [SerializeField] private HabitatDefinition habitat;
    [SerializeField] private string modelResource;
    [SerializeField] private Vector3 groundPosition;
    [SerializeField, Min(0.1f)] private float modelSize = 2f;

    private GameObject resident;

    private void OnEnable()
    {
        HabitatRecords.FirstDiscovered += OnDiscovered;
        HabitatRecords.Restored += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        HabitatRecords.FirstDiscovered -= OnDiscovered;
        HabitatRecords.Restored -= Refresh;
    }

    private void OnDiscovered(string id)
    {
        if (habitat != null && id == habitat.Id) Refresh();
    }

    private void Refresh()
    {
        bool visible = habitat != null && HabitatRecords.IsDiscovered(habitat.Id);
        if (resident != null)
        {
            resident.SetActive(visible);
            return;
        }
        if (!visible) return;

        var model = Resources.Load<GameObject>(modelResource);
        if (model == null)
        {
            Debug.LogError($"Habitat model missing at Resources/{modelResource}.", this);
            return;
        }

        resident = Instantiate(model, transform, false);
        resident.name = model.name;
        Vector3 perch = transform.TransformPoint(groundPosition);
        resident.transform.position = perch;
        // The two supplied FBXs use different units and pivots. Fit their rendered bounds once.
        var renderers = resident.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = GetBounds(renderers);
        float longestSide = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longestSide <= Mathf.Epsilon) return;
        resident.transform.localScale *= modelSize / longestSide;
        bounds = GetBounds(renderers);
        Vector3 feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        resident.transform.position += perch - feet;
    }

    private static Bounds GetBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
