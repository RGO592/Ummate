using UnityEngine;
using UnityEngine.InputSystem;

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
    [SerializeField] private EcoPlacementController placement;
    [SerializeField, Tooltip("Player root. Its movement / interaction / rigidbody are paused in build mode.")]
    private GameObject player;
    [SerializeField, Tooltip("Shown only in build mode (bottom bar, build top bar).")]
    private GameObject[] buildModeObjects = new GameObject[0];
    [SerializeField, Tooltip("Shown only while exploring (build button, return button).")]
    private GameObject[] exploreModeObjects = new GameObject[0];

    [Header("Build Camera")]
    [SerializeField, Range(60f, 90f), Tooltip("90 = straight down.")]
    private float buildPitch = 80f;
    [SerializeField, Min(0f), Tooltip("Extra space around the grid when framing.")]
    private float framingMargin = 4f;
    [SerializeField, Min(1f)] private float panSpeed = 35f;
    [SerializeField, Min(0f), Tooltip("How far the view may pan beyond the grid edge.")]
    private float panLimit = 15f;
    [SerializeField, Min(0.1f)] private float zoomStep = 5f;
    [SerializeField, Min(1f)] private float minZoomDistance = 15f;
    [SerializeField, Min(1f)] private float maxZoomDistance = 120f;

    private PlayerMovement playerMovement;
    private PlayerInteractor playerInteractor;
    private Rigidbody playerBody;
    private Vector3 focus;
    private float distance;

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
        if (IsBuildMode) UpdateBuildCamera(keyboard, Mouse.current);
    }

    // UI buttons
    public void ToggleBuildMode() => Apply(!IsBuildMode);
    public void EnterBuildMode() => Apply(true);
    public void ExitBuildMode() => Apply(false);

    private void Apply(bool build)
    {
        IsBuildMode = build;
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
        focus = grid.GridRoot.position;
        Vector2 half = grid.HalfExtents;
        float extent = Mathf.Max(half.x, half.y) + framingMargin;
        float vertical = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontal = Mathf.Atan(Mathf.Tan(vertical) * viewCamera.aspect);
        distance = Mathf.Clamp(extent / Mathf.Tan(Mathf.Min(vertical, horizontal)), minZoomDistance, maxZoomDistance);
        PlaceCamera();
    }

    private void UpdateBuildCamera(Keyboard keyboard, Mouse mouse)
    {
        if (viewCamera == null || grid == null) return;
        if (keyboard != null)
        {
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            focus += new Vector3(x, 0f, z) * (panSpeed * Time.unscaledDeltaTime);
            Vector3 center = grid.GridRoot.position;
            Vector2 half = grid.HalfExtents;
            focus.x = Mathf.Clamp(focus.x, center.x - half.x - panLimit, center.x + half.x + panLimit);
            focus.z = Mathf.Clamp(focus.z, center.z - half.y - panLimit, center.z + half.y + panLimit);
        }
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minZoomDistance, maxZoomDistance);
        }
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        Quaternion rotation = Quaternion.Euler(buildPitch, 0f, 0f);
        viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
    }
}
