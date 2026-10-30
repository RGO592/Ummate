using UnityEngine;

// One player-placeable village facility (밭, 꽃 화단, 나무). Square-grid village building only;
// the spirit realm's Hex placement (EcoGridManager) is a separate system.
[CreateAssetMenu(fileName = "VillageFacility", menuName = "Ummate/Village Facility")]
public sealed class VillageFacilityDefinition : ScriptableObject
{
    [SerializeField, Tooltip("Stable id for future saves. Never reuse or change after release.")]
    private string id;
    [SerializeField] private string displayName;
    [SerializeField, Tooltip("Instantiated on placement. Its pivot sits at the footprint's ground centre and it carries the functional component (FarmPlot, FlowerBed, ChoppableTree).")]
    private GameObject prefab;
    [SerializeField, Tooltip("Cells occupied: x = world X, y = world Z. 1 cell = 1 m.")]
    private Vector2Int footprint = Vector2Int.one;
    [SerializeField, Tooltip("Optional. The palette offers the facility once this quest is Active or Completed. Empty = always available.")]
    private string requiredQuestId;

    public string Id => id;
    public string DisplayName => displayName;
    public GameObject Prefab => prefab;
    public Vector2Int Footprint => new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
    public string RequiredQuestId => requiredQuestId;

    // Rotation is counted in 90 degree steps (0-3): odd steps swap the footprint's sides.
    public const int RotationStepCount = 4;
    public Vector2Int FootprintAt(int rotationSteps)
    {
        Vector2Int size = Footprint;
        return rotationSteps % 2 == 0 ? size : new Vector2Int(size.y, size.x);
    }
    public Quaternion RotationAt(int rotationSteps)
    {
        Quaternion baseRotation = prefab != null ? prefab.transform.rotation : Quaternion.identity;
        return Quaternion.Euler(0f, 90f * rotationSteps, 0f) * baseRotation;
    }
}
