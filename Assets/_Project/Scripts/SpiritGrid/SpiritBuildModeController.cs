using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Switches the spirit realm between exploring (player + quarter-view follow camera)
// and build mode (player frozen, camera snaps to a top view over the grid, placement and build UI on).
[DisallowMultipleComponent]
public sealed class SpiritBuildModeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera viewCamera;
    [SerializeField, Tooltip("Quarter-view follow used while exploring. Disabled in build mode.")]
    private CameraFollow exploreFollow;
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private EcoBuildAreaView buildArea;
    [SerializeField] private EcoPlacementController placement;
    [SerializeField, Tooltip("Player root. Its movement / interaction / rigidbody are paused in build mode.")]
    private GameObject player;
    [SerializeField, Tooltip("Shown only in build mode (bottom bar, build top bar).")]
    private GameObject[] buildModeObjects = new GameObject[0];
    [SerializeField, Tooltip("Shown only while exploring (build button, return button).")]
    private GameObject[] exploreModeObjects = new GameObject[0];

    [Header("Build Camera")]
    [SerializeField, Range(60f, 85f), Tooltip("Below 90 degrees gives the orbit a visible horizontal radius.")]
    private float buildPitch = 80f;
    [SerializeField, Min(0f), Tooltip("Extra space around the grid when framing.")]
    private float framingMargin = 4f;
    [SerializeField, Min(1f)] private float panSpeed = 35f;
    [SerializeField, Min(0.1f)] private float zoomStep = 5f;
    [SerializeField, Min(1f)] private float minZoomDistance = 15f;
    [SerializeField, Min(1f)] private float maxZoomDistance = 120f;

    [Header("Build Camera Orbit")]
    [SerializeField, Tooltip("Reference centre. WASD adds a view offset without moving this Transform or the grid.")]
    private Transform rotationPivot;
    [SerializeField, Min(0.01f)] private float rotationDuration = 0.35f;
    [SerializeField] private AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Build Camera Mouse Pan (left-drag on empty ground, middle-drag anywhere but UI)")]
    [SerializeField, Min(0.1f), Tooltip("1 = the ground under the cursor follows it exactly, so zooming out pans farther per pixel.")]
    private float mousePanSpeed = 1f;
    [SerializeField, Min(0f), Tooltip("Extra pan room beyond the Hex map. Background Hexes cover the view well past this.")]
    private float panBoundsMargin = 0f;
    [SerializeField, Tooltip("Limit the camera focus to panBoundsMin/Max (world X/Z) instead of the Hex map bounds.")]
    private bool useCustomPanBounds;
    [SerializeField] private Vector2 panBoundsMin = new Vector2(-150f, -150f);
    [SerializeField] private Vector2 panBoundsMax = new Vector2(50f, 50f);

    private PlayerMovement playerMovement;
    private PlayerInteractor playerInteractor;
    private Rigidbody playerBody;
    private Vector3 focus;
    private float distance;
    private int quarterTurns;
    private float currentYaw;
    private Vector3 panOffset;
    private bool orbiting;
    private float orbitElapsed, orbitDuration, orbitStartYaw, orbitDistance, orbitPitch;
    private Vector3 orbitFocus;
    private ButtonControl panButton;
    private Vector2 lastPanScreen;

    private Vector3 PivotPosition => rotationPivot != null ? rotationPivot.position : grid.ActiveBounds.center;

    public bool IsBuildMode { get; private set; }

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (player != null)
        {
            playerMovement = player.GetComponent<PlayerMovement>();
            playerInteractor = player.GetComponent<PlayerInteractor>();
            playerBody = player.GetComponent<Rigidbody>();
        }
    }

    private void Start()
    {
        Apply(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.bKey.wasPressedThisFrame) ToggleBuildMode();
        if (IsBuildMode && (buildArea == null || !buildArea.InputBlocked)) UpdateBuildCamera(keyboard, Mouse.current);
    }

    // UI buttons
    public void ToggleBuildMode() => Apply(!IsBuildMode);
    public void EnterBuildMode() => Apply(true);
    public void ExitBuildMode() => Apply(false);

    private void Apply(bool build)
    {
        orbiting = false;
        panButton = null;
        IsBuildMode = build;
        if (buildArea != null) buildArea.SetVisible(build);
        if (playerMovement != null) playerMovement.enabled = !build;
        if (playerInteractor != null) playerInteractor.enabled = !build;
        if (build && playerBody != null) playerBody.linearVelocity = new Vector3(0f, playerBody.linearVelocity.y, 0f);
        if (placement != null) placement.enabled = build;
        // Re-enabling the follow camera re-centres it on the player immediately.
        if (exploreFollow != null) exploreFollow.enabled = !build;
        foreach (GameObject go in buildModeObjects) if (go != null) go.SetActive(build);
        foreach (GameObject go in exploreModeObjects) if (go != null) go.SetActive(!build);
        if (build) FrameGrid();
    }

    // Instant switch to a top view that fits the whole grid.
    private void FrameGrid()
    {
        if (viewCamera == null || grid == null) return;
        focus = PivotPosition;
        panOffset = Vector3.zero;
        currentYaw = quarterTurns * 90f;
        Vector3 half = grid.ActiveBounds.extents;
        // Circumscribed radius keeps all corners in frame through the 90-degree orbit.
        float extent = new Vector2(half.x, half.z).magnitude + framingMargin;
        float vertical = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontal = Mathf.Atan(Mathf.Tan(vertical) * viewCamera.aspect);
        distance = Mathf.Clamp(extent / Mathf.Tan(Mathf.Min(vertical, horizontal)), minZoomDistance, maxZoomDistance);
        PlaceCamera();
    }

    private void UpdateBuildCamera(Keyboard keyboard, Mouse mouse)
    {
        if (viewCamera == null || grid == null) return;
        // Freeze pan, zoom and repeated rotation requests until the fixed-radius arc finishes.
        if (orbiting) { panButton = null; AdvanceOrbit(); return; }
        focus = PivotPosition + panOffset;
        UpdateMousePan(mouse);
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
            {
                BeginOrbit();
                AdvanceOrbit();
                return;
            }
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            // Screen right/up projected onto the ground, including the straight-down view.
            // Using camera.forward here would degenerate at a 90-degree pitch.
            Quaternion heading = Quaternion.Euler(0f, currentYaw, 0f);
            focus += heading * new Vector3(x, 0f, z) * (panSpeed * Time.unscaledDeltaTime);
        }
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minZoomDistance, maxZoomDistance);
        }
        PlaceCamera();
    }

    // Grab-the-map pan: the ground point under the cursor stays under it. Both rays use the current
    // camera pose, so the move follows the screen at every orbit angle and scales with zoom.
    private void UpdateMousePan(Mouse mouse)
    {
        if (mouse == null) { panButton = null; return; }
        Vector2 screen = mouse.position.ReadValue();
        if (panButton == null)
        {
            // Middle: pan anywhere but UI. Left: only where the placement controller would do nothing.
            if (mouse.middleButton.wasPressedThisFrame && !PointerOverUI(screen)) panButton = mouse.middleButton;
            else if (mouse.leftButton.wasPressedThisFrame && (placement == null ? !PointerOverUI(screen) : placement.CanStartLeftPan(screen)))
                panButton = mouse.leftButton;
            lastPanScreen = screen;
            return;
        }
        // A Hex pick or remove mode that starts during a left drag takes the button back.
        if (!panButton.isPressed || (panButton == mouse.leftButton && placement != null && (placement.IsDragging || placement.RemoveMode)))
        {
            panButton = null;
            return;
        }
        if (screen != lastPanScreen && TryGroundPoint(lastPanScreen, out Vector3 from) && TryGroundPoint(screen, out Vector3 to))
            focus += Vector3.ProjectOnPlane(from - to, grid.GridRoot.up) * mousePanSpeed;
        lastPanScreen = screen;
    }

    private bool TryGroundPoint(Vector2 screen, out Vector3 point)
    {
        Ray ray = viewCamera.ScreenPointToRay(screen);
        var plane = new Plane(grid.GridRoot.up, grid.GridRoot.position);
        bool hit = plane.Raycast(ray, out float enter);
        point = hit ? ray.GetPoint(enter) : default;
        return hit;
    }

    private bool PointerOverUI(Vector2 screen)
    {
        if (placement != null) return placement.IsPointerOverUI(screen);
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private Vector3 ClampFocus(Vector3 value)
    {
        if (useCustomPanBounds)
        {
            value.x = Mathf.Clamp(value.x, Mathf.Min(panBoundsMin.x, panBoundsMax.x), Mathf.Max(panBoundsMin.x, panBoundsMax.x));
            value.z = Mathf.Clamp(value.z, Mathf.Min(panBoundsMin.y, panBoundsMax.y), Mathf.Max(panBoundsMin.y, panBoundsMax.y));
            return value;
        }
        return buildArea != null ? buildArea.ClampCameraFocus(value, panBoundsMargin) : value;
    }

    private void BeginOrbit()
    {
        focus = ClampFocus(focus);
        orbitFocus = focus;
        orbitDistance = distance;
        orbitPitch = buildPitch;
        orbitStartYaw = currentYaw;
        orbitElapsed = 0;
        orbitDuration = Mathf.Max(0.01f, rotationDuration);
        orbiting = true;
    }

    private void AdvanceOrbit()
    {
        orbitElapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(orbitElapsed / orbitDuration);
        float eased = rotationCurve != null ? Mathf.Clamp01(rotationCurve.Evaluate(t)) : Mathf.SmoothStep(0, 1, t);
        if (t >= 1f) eased = 1f;
        // Interpolate the angle, not camera XYZ: this follows a circular arc with constant
        // height and radius instead of cutting across the centre on a straight-line chord.
        currentYaw = orbitStartYaw + 90f * eased;
        focus = orbitFocus;
        distance = orbitDistance;
        PlaceCamera();
        if (t >= 1f)
        {
            quarterTurns = (quarterTurns + 1) % 4;
            currentYaw = quarterTurns * 90f;
            orbiting = false;
        }
    }

    private void PlaceCamera()
    {
        if (!orbiting) focus = ClampFocus(focus);
        panOffset = focus - PivotPosition;
        Quaternion rotation = Quaternion.Euler(orbiting ? orbitPitch : buildPitch, currentYaw, 0f);
        viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
    }

    // Disabled by a modal input lock (pause menu, quest window) mid-rotation: land on the target quarter
    // turn instead of freezing between angles. Build mode itself is left as it was.
    private void OnDisable()
    {
        panButton = null;
        if (!orbiting) return;
        if (viewCamera == null || grid == null) { orbiting = false; return; }
        orbitElapsed = orbitDuration;
        AdvanceOrbit();
    }
}
