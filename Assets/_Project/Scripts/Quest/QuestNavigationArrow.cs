using UnityEngine;

// Small ground arrow beside the player's feet, pointing at the current main objective's target in world XZ.
// Everything is re-read from QuestLog and the enabled targets each frame, so objective changes, placed or
// moved facilities and loaded saves apply at once and nothing needs saving.
[DisallowMultipleComponent]
public sealed class QuestNavigationArrow : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField, Tooltip("The arrow's look; shown and hidden as a whole. A MeshFilter without a mesh gets the temporary arrow shape.")]
    private GameObject visual;
    [SerializeField] private PauseMenu pauseMenu;
    [SerializeField] private QuestWindow questWindow;

    [Header("Temporary values")]
    [SerializeField, Min(0f), Tooltip("Hidden this close to the target, in m.")]
    private float hideDistance = 2.5f;
    [SerializeField, Min(0f), Tooltip("Extra distance before it shows again, so it does not flicker at the edge.")]
    private float showMargin = 0.5f;
    [SerializeField, Min(0f), Tooltip("Distance from the player's centre toward the target, in m.")]
    private float orbitRadius = 1.3f;
    [SerializeField, Min(0f), Tooltip("Height above the player's feet, over the ground and paths.")]
    private float groundLift = 0.08f;

    private const float QuestSearchInterval = 1f;

    private QuestLog quests;
    private float nextQuestSearch;
    private Collider playerCollider;
    private QuestNavigationTarget lastTarget;
    private bool arrived;
    private Mesh generatedMesh;

    private void Awake()
    {
        if (player != null) playerCollider = player.GetComponent<Collider>();
        if (visual == null) return;
        if (visual.TryGetComponent(out MeshFilter filter) && filter.sharedMesh == null)
            filter.sharedMesh = generatedMesh = BuildArrowMesh();
        visual.SetActive(false);
    }

    private void OnDestroy()
    {
        if (generatedMesh != null) Destroy(generatedMesh);
    }

    private void LateUpdate()
    {
        if (visual == null || player == null) return;
        QuestNavigationTarget target = Blocked ? null : FindCurrentTarget();
        if (target != lastTarget) arrived = false;
        lastTarget = target;
        if (target == null)
        {
            visual.SetActive(false);
            return;
        }

        Vector3 from = player.position;
        arrived = target.DistanceFrom(from) <= hideDistance + (arrived ? showMargin : 0f);
        Vector3 direction = target.transform.position - from;
        direction.y = 0f;
        if (arrived || direction.sqrMagnitude < 0.0001f)
        {
            visual.SetActive(false);
            return;
        }

        direction.Normalize();
        float feet = playerCollider != null ? playerCollider.bounds.min.y : from.y;
        Vector3 position = new Vector3(from.x, feet + groundLift, from.z) + direction * orbitRadius;
        visual.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction, Vector3.up));
        visual.SetActive(true);
    }

    private bool Blocked =>
        SceneTransitionService.IsTraveling ||
        (pauseMenu != null && pauseMenu.IsOpen) || (questWindow != null && questWindow.IsOpen);

    private QuestLog Quests
    {
        get
        {
            // The log lives on the village player, in another scene while a region is visited.
            if (quests == null && Time.unscaledTime >= nextQuestSearch)
            {
                nextQuestSearch = Time.unscaledTime + QuestSearchInterval;
                foreach (QuestLog found in FindObjectsByType<QuestLog>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    quests = found;
                    break;
                }
            }
            return quests;
        }
    }

    // Null means no arrow: no quest, an objective without a place, or a target that cannot be reached from here.
    private QuestNavigationTarget FindCurrentTarget()
    {
        QuestLog.Entry main = Quests != null ? Quests.CurrentMain : null;
        int index = main != null ? main.CurrentObjective : -1;
        if (index < 0) return null;
        QuestDefinition.Objective objective = main.Definition.Objectives[index];
        if (objective == null || string.IsNullOrEmpty(objective.navigationTargetId)) return null;

        UnityEngine.SceneManagement.Scene here = gameObject.scene;
        if (!string.IsNullOrEmpty(objective.navigationScene) && objective.navigationScene != here.name)
            return QuestNavigationTarget.FindExitTo(here, objective.navigationScene, player.position);
        return QuestNavigationTarget.FindNearest(here, objective.navigationTargetId, player.position);
    }

    // Flat arrow pointing along +Z, about 1.1 m long.
    private static Mesh BuildArrowMesh()
    {
        var mesh = new Mesh { name = "QuestNavigationArrow" };
        mesh.vertices = new[]
        {
            new Vector3(0f, 0f, 0.6f), new Vector3(0.45f, 0f, 0.05f), new Vector3(-0.45f, 0f, 0.05f),
            new Vector3(-0.17f, 0f, 0.05f), new Vector3(0.17f, 0f, 0.05f), new Vector3(0.17f, 0f, -0.5f), new Vector3(-0.17f, 0f, -0.5f)
        };
        mesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 3, 5, 6 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
