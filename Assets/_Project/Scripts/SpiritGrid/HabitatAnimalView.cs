using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// A visual resident of the forest. Discovery persists when the player rearranges the Hexes.
// Place At Habitat: the animal lives only on the Hexes that currently form its habitat (HabitatRecords.CellsOf;
// for the deer the four Tree / Rock / Mushroom / Flower cells) and never steps onto any other cell. While the
// habitat does not stand (taken apart, or before the grid was read) it is hidden; rebuilt, it returns.
// Off: the fixed Ground Position below (local to this object), as before.
public sealed class HabitatAnimalView : MonoBehaviour
{
    [SerializeField] private HabitatDefinition habitat;
    [SerializeField] private string modelResource;
    [SerializeField, Tooltip("Live on the habitat's own Hexes. Any animal can use this.")]
    private bool placeAtHabitat;
    [SerializeField, Tooltip("The grid the habitat is built on. Empty = EcoGridManager on this object.")]
    private EcoGridManager grid;
    [SerializeField, Tooltip("Fixed position (local to this object) when Place At Habitat is off.")]
    private Vector3 groundPosition;
    [SerializeField, Min(0.1f)] private float modelSize = 2f;

    [Header("Wander (Place At Habitat; no animation clips needed)")]
    [SerializeField, Tooltip("Walk between points on the habitat's Hexes, pause, walk again, with a small step bob.")]
    private bool wander;
    [SerializeField, Tooltip("Yaw added so the model's front faces where it walks. The deer model faces +X: -90.")]
    private float facingOffset;
    [SerializeField, Min(0.01f)] private float walkSpeed = 1.2f;
    [SerializeField, Min(1f), Tooltip("Degrees per second while turning toward the next point.")]
    private float turnSpeed = 240f;
    [SerializeField, Min(0f)] private float minPause = 1.5f;
    [SerializeField, Min(0f)] private float maxPause = 4f;
    [SerializeField, Min(0f), Tooltip("Height of the hop on each step.")]
    private float stepBob = 0.08f;
    [SerializeField, Min(0.1f)] private float stepsPerSecond = 3f;
    [SerializeField, Tooltip("Feet above the grid plane on top of the tiles.")]
    private float standHeight = 0.03f;

    [Header("Animation (from the model file)")]
    [SerializeField, Tooltip("Take played on a loop, matched by name (Blender's 'metarig|idle' matches 'idle'). A model without such a take (deer, otter) uses no animation.")]
    private string loopClip = "idle";

    private GameObject resident;
    private Vector3 pivotFromFeet;
    private Quaternion authoredRotation;
    private Vector3 feet;
    private float yaw, pause, stepPhase;
    private bool placed;
    private readonly Queue<Vector3> path = new Queue<Vector3>();
    private readonly HashSet<HexCoord> cellSet = new HashSet<HexCoord>();
    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private double clipLength;

    private EcoGridManager Map => grid != null ? grid : grid = GetComponent<EcoGridManager>();

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    // The imported takes are not marked to loop, so the time wraps here: the idle plays on forever.
    private void LateUpdate()
    {
        if (!graph.IsValid() || clipLength <= 0d) return;
        double time = clipPlayable.GetTime();
        if (time >= clipLength) clipPlayable.SetTime(time % clipLength);
    }

    // Plays the model's looping take on its imported Animator. False when the model has none (no animation).
    private bool StartLoopClip(Animator animator)
    {
        if (animator == null || string.IsNullOrEmpty(loopClip)) return false;
        AnimationClip chosen = null;
        foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(modelResource))
        {
            if (clip == null || clip.name.StartsWith("__preview__")) continue;
            string take = clip.name.Substring(clip.name.LastIndexOf('|') + 1);
            if (!string.Equals(take, loopClip, System.StringComparison.OrdinalIgnoreCase)) continue;
            // Prefer the armature-bound take ("metarig|idle") over the scene-level one ("idle").
            if (chosen == null || (clip.name.Contains("|") && !chosen.name.Contains("|"))) chosen = clip;
        }
        if (chosen == null) return false;
        animator.enabled = true;
        animator.applyRootMotion = false;
        graph = PlayableGraph.Create($"{name} {chosen.name}");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        clipPlayable = AnimationClipPlayable.Create(graph, chosen);
        AnimationPlayableOutput.Create(graph, "Animal", animator).SetSourcePlayable(clipPlayable);
        clipLength = chosen.length;
        // Each animal starts at its own point of the loop.
        clipPlayable.SetTime(Random.Range(0f, Mathf.Max(0.01f, chosen.length)));
        graph.Play();
        return true;
    }

    private void OnEnable()
    {
        HabitatRecords.FirstDiscovered += OnHabitatChanged;
        HabitatRecords.GroupChanged += OnHabitatChanged;
        HabitatRecords.Restored += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        HabitatRecords.FirstDiscovered -= OnHabitatChanged;
        HabitatRecords.GroupChanged -= OnHabitatChanged;
        HabitatRecords.Restored -= Refresh;
    }

    private void OnHabitatChanged(string id)
    {
        if (habitat != null && id == habitat.Id) Refresh();
    }

    private void Refresh()
    {
        if (habitat == null) return;
        cellSet.Clear();
        if (placeAtHabitat) foreach (HexCoord coord in HabitatRecords.CellsOf(habitat.Id)) cellSet.Add(coord);
        bool visible = HabitatRecords.IsDiscovered(habitat.Id) && (!placeAtHabitat || (cellSet.Count > 0 && Map != null));
        if (resident == null && (!visible || !CreateResident())) return;
        if (visible)
        {
            // The habitat changed under it: stop, and stay put only if it still stands on one of the cells.
            path.Clear();
            pause = Random.Range(0f, minPause);
            if (!placeAtHabitat) feet = transform.TransformPoint(groundPosition);
            else if (!placed || !cellSet.Contains(CellAt(feet))) feet = Centre(NearestCell(feet));
            else if (!WellInside(feet)) feet = Centre(CellAt(feet)); // Was crossing toward a cell that just left.
            placed = true;
            Pose(0f);
        }
        resident.SetActive(visible);
    }

    private void Update()
    {
        if (!wander || !placeAtHabitat || resident == null || !resident.activeSelf || cellSet.Count == 0) return;
        float dt = Time.deltaTime;
        if (path.Count == 0)
        {
            pause -= dt;
            if (pause > 0f) return;
            PlanWalk();
            if (path.Count == 0) { pause = NextPause(); return; }
        }
        Vector3 waypoint = path.Peek();
        Vector3 toWaypoint = waypoint - feet;
        toWaypoint.y = 0f;
        if (toWaypoint.sqrMagnitude < 0.0025f)
        {
            feet = waypoint;
            path.Dequeue();
            if (path.Count == 0) { pause = NextPause(); Pose(0f); }
            return;
        }
        // Turn first, then walk: no sliding sideways.
        float wantedYaw = Mathf.Atan2(toWaypoint.x, toWaypoint.z) * Mathf.Rad2Deg + facingOffset;
        yaw = Mathf.MoveTowardsAngle(yaw, wantedYaw, turnSpeed * dt);
        float bob = 0f;
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, wantedYaw)) < 25f)
        {
            feet = Vector3.MoveTowards(feet, waypoint, walkSpeed * dt);
            stepPhase += dt * stepsPerSecond;
            bob = Mathf.Abs(Mathf.Sin(stepPhase * Mathf.PI)) * stepBob;
        }
        Pose(bob);
    }

    private float NextPause() => Random.Range(minPause, Mathf.Max(minPause, maxPause));

    // Route: back to the centre of the cell it stands on, then centre to centre across habitat cells only (each
    // step crosses the edge two habitat cells share), then a point well inside the target cell. Every leg stays
    // inside habitat Hexes, and the body (half its size from the feet) never reaches a cell outside the habitat.
    private void PlanWalk()
    {
        path.Clear();
        HexCoord here = CellAt(feet);
        if (!cellSet.Contains(here)) { feet = Centre(NearestCell(feet)); here = CellAt(feet); }
        var targets = new List<HexCoord>(cellSet);
        HexCoord goal = targets[Random.Range(0, targets.Count)];
        List<HexCoord> route = Route(here, goal);
        if (route == null) return;
        path.Enqueue(Centre(here));
        foreach (HexCoord step in route) path.Enqueue(Centre(step));
        float inner = Mathf.Max(0f, Map.CellSize * 0.5f - Margin);
        Vector2 offset = Random.insideUnitCircle * inner;
        path.Enqueue(Centre(goal) + new Vector3(offset.x, 0f, offset.y));
    }

    // Breadth-first over neighbouring habitat cells (excluding the start); null if unreachable.
    private List<HexCoord> Route(HexCoord from, HexCoord to)
    {
        var route = new List<HexCoord>();
        if (from == to) return route;
        var previous = new Dictionary<HexCoord, HexCoord> { { from, from } };
        var frontier = new Queue<HexCoord>();
        frontier.Enqueue(from);
        while (frontier.Count > 0)
        {
            HexCoord current = frontier.Dequeue();
            if (current == to) break;
            for (int d = 0; d < HexCoord.Directions.Count; d++)
            {
                HexCoord next = current.Neighbor(d);
                if (!cellSet.Contains(next) || previous.ContainsKey(next)) continue;
                previous[next] = current;
                frontier.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return null;
        for (HexCoord step = to; step != from; step = previous[step]) route.Add(step);
        route.Reverse();
        return route;
    }

    // Half the fitted model, capped so a little room is always left in the middle of a Hex.
    private float Margin => Mathf.Min(modelSize * 0.5f, Map.CellSize * 0.4f);

    private HexCoord CellAt(Vector3 world) => HexMetrics.WorldToHex(Map.GridRoot, world, Map.CellSize);

    // Far enough from every edge of its cell that the body cannot reach a neighbouring cell.
    private bool WellInside(Vector3 world)
    {
        Vector3 offset = world - Map.GridToWorld(CellAt(world));
        offset.y = 0f;
        return offset.magnitude <= Map.CellSize * 0.5f - Margin + 0.001f;
    }

    private Vector3 Centre(HexCoord coord) => Map.GridToWorld(coord) + Map.GridRoot.up * standHeight;

    private HexCoord NearestCell(Vector3 world)
    {
        HexCoord best = default;
        float bestDistance = float.PositiveInfinity;
        foreach (HexCoord coord in cellSet)
        {
            float distance = (Map.GridToWorld(coord) - world).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = coord; }
        }
        return best;
    }

    // Rotates around the model's visual centre (not its imported pivot), feet on the ground plus the step hop.
    private void Pose(float bob)
    {
        Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
        resident.transform.SetPositionAndRotation(feet + Vector3.up * bob + turn * pivotFromFeet, turn * authoredRotation);
    }

    private bool CreateResident()
    {
        var model = Resources.Load<GameObject>(modelResource);
        if (model == null)
        {
            Debug.LogError($"Habitat model missing at Resources/{modelResource}.", this);
            return false;
        }
        resident = Instantiate(model, transform, false);
        resident.name = model.name;
        // A model with a looping take (the squirrel's idle) plays it on its Animator; one without animation (deer,
        // otter) has an Animator with no controller that would only hold the bind pose, so that one is switched off.
        Animator[] animators = resident.GetComponentsInChildren<Animator>(true);
        bool animated = StartLoopClip(animators.Length > 0 ? animators[0] : null);
        for (int i = animated ? 1 : 0; i < animators.Length; i++) animators[i].enabled = false;
        authoredRotation = resident.transform.rotation;
        yaw = 0f;
        pivotFromFeet = Vector3.zero;
        // The supplied FBXs use different units and pivots. Fit their rendered bounds once and remember where the
        // feet sit relative to the pivot, so the resident can be moved and turned anywhere afterwards.
        var renderers = resident.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return true;
        Bounds bounds = GetBounds(renderers);
        float longestSide = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longestSide > Mathf.Epsilon) resident.transform.localScale *= modelSize / longestSide;
        bounds = GetBounds(renderers);
        Vector3 feetPoint = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        pivotFromFeet = resident.transform.position - feetPoint;
        return true;
    }

    private static Bounds GetBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
