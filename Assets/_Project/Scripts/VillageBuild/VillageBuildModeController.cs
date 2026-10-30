using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Village living-facility build mode: B toggles it, the palette picks a facility, the ghost snaps to the
// 1 m grid, R turns it 90 degrees, left click places, right click / Esc cancels. Placed facilities are the ordinary functional
// prefabs, so F interaction, quest events and feedback come from FarmPlot / FlowerBed / ChoppableTree.
// A modal window disables this component through its input list: build mode stays on, its input pauses.
[DisallowMultipleComponent]
public sealed class VillageBuildModeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VillageBuildArea area;
    [SerializeField] private VillageBuildPalette palette;
    [SerializeField] private VillageBuildGhost ghost;
    [SerializeField] private Camera viewCamera;
    [SerializeField, Tooltip("Quarter-view follow, disabled while building. Re-enabling re-centres it on the player.")]
    private CameraFollow exploreFollow;
    [SerializeField, Tooltip("Handed to placed FarmPlot / FlowerBed instances; prefabs cannot reference scene objects.")]
    private GameDay gameDay;
    [SerializeField, Tooltip("Optional. Reads facility unlocks (VillageFacilityDefinition.requiredQuestId).")]
    private QuestLog quests;
    [SerializeField] private Rigidbody playerBody;
    [SerializeField, Tooltip("Player input paused in build mode: movement, F interaction, quickbar.")]
    private Behaviour[] playerInputs = new Behaviour[0];

    [Header("Facilities")]
    [SerializeField] private VillageFacilityDefinition[] facilities = new VillageFacilityDefinition[0];
    [SerializeField, Tooltip("Parent for placed instances.")]
    private Transform placedRoot;

    [Header("Build Camera")]
    [SerializeField, Range(30f, 85f)] private float buildPitch = 60f;
    [SerializeField] private float buildYaw = 45f;
    [SerializeField, Min(1f)] private float buildDistance = 26f;
    [SerializeField, Min(1f)] private float minDistance = 12f;
    [SerializeField, Min(1f)] private float maxDistance = 45f;
    [SerializeField, Min(0.1f)] private float zoomStep = 3f;
    [SerializeField, Min(1f)] private float panSpeed = 18f;

    private readonly List<VillagePlacedFacility> placed = new List<VillagePlacedFacility>();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private PointerEventData pointerData;
    private EventSystem pointerOwner;
    private bool[] inputWasEnabled;
    private VillageFacilityDefinition selected;
    private VillagePlacedFacility moving;
    private int carriedRotation;
    private Vector3 focus;
    private float distance;
    private Vector2 lastMiddleDrag;

    public bool IsBuildMode { get; private set; }

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (placedRoot == null) placedRoot = transform;
        if (palette != null)
        {
            palette.FacilityPicked += Select;
            palette.ExitRequested += ExitBuildMode;
        }
    }

    private void OnDestroy()
    {
        if (palette == null) return;
        palette.FacilityPicked -= Select;
        palette.ExitRequested -= ExitBuildMode;
    }

    private void Update()
    {
        if (!Application.isFocused || SceneTransitionService.IsTraveling || Time.timeScale == 0f) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.bKey.wasPressedThisFrame)
        {
            if (IsBuildMode) ExitBuildMode();
            else EnterBuildMode();
            return;
        }
        if (!IsBuildMode) return;

        // Consumed so the same Esc press does not also open the pause menu.
        if (UIEscape.WasPressed)
        {
            UIEscape.Consume();
            if (Carrying) CancelCarry();
            else ExitBuildMode();
            return;
        }
        UpdateCamera(keyboard, mouse);
        if (mouse == null) return;
        if (Carrying && mouse.rightButton.wasPressedThisFrame)
        {
            CancelCarry();
            return;
        }
        // UpdatePlacement below re-reads the cells, ghost and placement check from this in the same frame.
        if (Carrying && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            carriedRotation = (carriedRotation + 1) % VillageFacilityDefinition.RotationStepCount;
        UpdatePlacement(mouse);
    }

    private bool Carrying => selected != null || moving != null;
    private VillageFacilityDefinition Carried => moving != null ? moving.Definition : selected;

    // ---- Mode ----

    public void EnterBuildMode()
    {
        if (IsBuildMode || area == null || palette == null || ghost == null) return;
        IsBuildMode = true;

        inputWasEnabled = new bool[playerInputs.Length];
        for (int i = 0; i < playerInputs.Length; i++)
        {
            if (playerInputs[i] == null) continue;
            inputWasEnabled[i] = playerInputs[i].enabled;
            playerInputs[i].enabled = false;
        }
        // PlayerMovement is off now and no longer writes velocity: stop the walk it left behind.
        if (playerBody != null && !playerBody.isKinematic)
        {
            Vector3 velocity = playerBody.linearVelocity;
            playerBody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }

        if (exploreFollow != null) exploreFollow.enabled = false;
        focus = ClampFocus(playerBody != null ? playerBody.position : area.FootprintCenter(area.Cells));
        distance = Mathf.Clamp(buildDistance, minDistance, maxDistance);
        PlaceCamera();

        area.SetGridVisible(true);
        palette.Show(facilities, IsUnlocked);
    }

    public void ExitBuildMode()
    {
        if (!IsBuildMode) return;
        CancelCarry();
        IsBuildMode = false;
        area.SetGridVisible(false);
        palette.Hide();
        if (exploreFollow != null) exploreFollow.enabled = true;
        for (int i = 0; i < playerInputs.Length; i++)
            if (playerInputs[i] != null && inputWasEnabled[i]) playerInputs[i].enabled = true;
        inputWasEnabled = null;
    }

    private bool IsUnlocked(VillageFacilityDefinition facility) =>
        string.IsNullOrEmpty(facility.RequiredQuestId) || quests == null ||
        quests.GetStatus(facility.RequiredQuestId) != QuestStatus.Locked;

    // ---- Selecting and placing ----

    private void Select(VillageFacilityDefinition facility)
    {
        CancelCarry();
        selected = facility;
        carriedRotation = 0;
        ghost.Show(facility);
        palette.SetSelected(facility);
    }

    private void BeginMove(VillagePlacedFacility facility)
    {
        moving = facility;
        carriedRotation = facility.RotationSteps;
        ghost.Show(facility.Definition);
        // Hidden, not destroyed: crop and flower state stay on the same instance.
        facility.gameObject.SetActive(false);
        palette.SetHint($"{facility.Definition.DisplayName} 옮기기: 좌클릭 설치   R: 회전   우클릭 / ESC: 취소");
    }

    // A moved facility keeps its position and rotation until FinishMove, so cancelling only shows it again.
    private void CancelCarry()
    {
        if (moving != null) moving.gameObject.SetActive(true);
        moving = null;
        selected = null;
        ghost.Hide();
        palette.SetSelected(null);
    }

    private void UpdatePlacement(Mouse mouse)
    {
        Vector2 screen = mouse.position.ReadValue();
        bool overUI = PointerOverUI(screen);
        bool hasPoint = area.TryGroundPoint(viewCamera.ScreenPointToRay(screen), out Vector3 point);
        if (!Carrying)
        {
            if (hasPoint && !overUI && mouse.leftButton.wasPressedThisFrame)
            {
                VillagePlacedFacility hovered = FindPlacedAt(VillageBuildArea.CellAt(point));
                if (hovered != null) BeginMove(hovered);
            }
            return;
        }
        if (!hasPoint) return;

        RectInt cells = area.FootprintAt(point, Carried.FootprintAt(carriedRotation));
        string blocker = FindBlocker(cells);
        ghost.MoveTo(area.FootprintCenter(cells), Carried.RotationAt(carriedRotation), blocker == null);
        if (overUI || !mouse.leftButton.wasPressedThisFrame) return;
        if (blocker != null)
        {
            PlayerFeedback.Notify(blocker);
            return;
        }
        if (moving != null) FinishMove(cells);
        else Place(selected, cells);
    }

    private string FindBlocker(RectInt cells)
    {
        foreach (VillagePlacedFacility facility in placed)
            if (facility != null && facility != moving && facility.Cells.Overlaps(cells)) return "이미 시설이 있습니다.";
        return area.FindStaticBlocker(cells);
    }

    private void Place(VillageFacilityDefinition facility, RectInt cells)
    {
        if (facility.Prefab == null)
        {
            Debug.LogWarning($"{facility.name}: 시설 Prefab 연결을 확인해 주세요.", this);
            return;
        }
        Spawn(facility, cells, carriedRotation);
        // Only a real placement counts: selecting, a refused click or moving a facility raise nothing.
        QuestEvents.Raise(QuestEventType.FacilityPlaced, null, facility.Id);
        // The selection stays so several can be placed in a row.
    }

    private VillagePlacedFacility Spawn(VillageFacilityDefinition facility, RectInt cells, int rotationSteps)
    {
        GameObject instance = Instantiate(facility.Prefab, area.FootprintCenter(cells), facility.RotationAt(rotationSteps), placedRoot);
        instance.name = $"{facility.Id}_{cells.xMin}_{cells.yMin}";
        if (instance.TryGetComponent(out FarmPlot plot)) plot.BindGameDay(gameDay);
        if (instance.TryGetComponent(out FlowerBed bed)) bed.BindGameDay(gameDay);
        if (instance.TryGetComponent(out TreePlot treePlot)) treePlot.BindGameDay(gameDay);
        VillagePlacedFacility record = instance.AddComponent<VillagePlacedFacility>();
        record.Place(facility, cells, rotationSteps);
        placed.Add(record);
        return record;
    }

    private void FinishMove(RectInt cells)
    {
        moving.Place(moving.Definition, cells, carriedRotation);
        moving.transform.SetPositionAndRotation(area.FootprintCenter(cells), moving.Definition.RotationAt(carriedRotation));
        moving.gameObject.SetActive(true);
        moving = null;
        ghost.Hide();
        palette.SetSelected(null);
    }

    private VillagePlacedFacility FindPlacedAt(Vector2Int cell)
    {
        foreach (VillagePlacedFacility facility in placed)
            if (facility != null && facility.Cells.Contains(cell)) return facility;
        return null;
    }

    // ---- Save / load ----

    public GameSaveData.VillageFacilityState[] CaptureState() => placed.Where(facility => facility != null).Select(facility =>
    {
        var state = new GameSaveData.VillageFacilityState
        {
            definitionId = facility.Definition.Id, x = facility.Cells.xMin, y = facility.Cells.yMin,
            rotationSteps = facility.RotationSteps
        };
        if (facility.TryGetComponent(out FarmPlot plot)) state.plot = plot.CaptureState(facility.name);
        if (facility.TryGetComponent(out FlowerBed bed)) state.flowerBed = bed.CaptureState(facility.name);
        if (facility.TryGetComponent(out TreePlot treePlot)) state.treePlot = treePlot.CaptureState();
        return state;
    }).ToArray();

    // Checked before any gameplay state changes. Scene obstacles are not re-tested: they were clear when placed.
    public bool TryValidateState(GameSaveData.VillageFacilityState[] saved, out string error)
    {
        error = null;
        var taken = new List<RectInt>();
        foreach (GameSaveData.VillageFacilityState state in saved)
        {
            VillageFacilityDefinition facility = state != null ? FindDefinition(state.definitionId) : null;
            if (facility == null || facility.Prefab == null)
            {
                error = $"저장 파일의 시설({state?.definitionId})이 현재 시설 목록에 없습니다.";
                return false;
            }
            if (state.rotationSteps < 0 || state.rotationSteps >= VillageFacilityDefinition.RotationStepCount)
            {
                error = "설치 시설 회전 값 오류";
                return false;
            }
            var cells = new RectInt(new Vector2Int(state.x, state.y), facility.FootprintAt(state.rotationSteps));
            RectInt bounds = area.Cells;
            if (cells.xMin < bounds.xMin || cells.yMin < bounds.yMin || cells.xMax > bounds.xMax || cells.yMax > bounds.yMax ||
                taken.Any(other => other.Overlaps(cells)))
            {
                error = "설치 시설 위치 오류";
                return false;
            }
            if (state.treePlot != null && (!System.Enum.IsDefined(typeof(TreePlot.PlotStage), state.treePlot.stage) || state.treePlot.growthDays < 0))
            {
                error = "나무터 상태 오류";
                return false;
            }
            if (state.flowerBed != null && !System.Enum.IsDefined(typeof(FlowerBed.BedState), state.flowerBed.state))
            {
                error = "화단 상태 오류";
                return false;
            }
            taken.Add(cells);
        }
        return true;
    }

    // Load / new game only: replaces every placed facility. Raises no quest events and grows nothing.
    public void RestoreState(GameSaveData.VillageFacilityState[] saved)
    {
        ExitBuildMode();
        foreach (VillagePlacedFacility facility in placed)
        {
            if (facility == null) continue;
            facility.gameObject.SetActive(false);
            Destroy(facility.gameObject);
        }
        placed.Clear();
        if (saved == null) return;
        foreach (GameSaveData.VillageFacilityState state in saved)
        {
            VillageFacilityDefinition facility = FindDefinition(state.definitionId);
            VillagePlacedFacility record = Spawn(facility, new RectInt(new Vector2Int(state.x, state.y), facility.FootprintAt(state.rotationSteps)), state.rotationSteps);
            if (record.TryGetComponent(out FarmPlot plot)) plot.RestoreState(state.plot);
            if (record.TryGetComponent(out FlowerBed bed)) bed.RestoreState(state.flowerBed);
            if (record.TryGetComponent(out TreePlot treePlot)) treePlot.RestoreState(state.treePlot);
        }
    }

    private VillageFacilityDefinition FindDefinition(string id) =>
        facilities.FirstOrDefault(facility => facility != null && facility.Id == id);

    // ---- Camera ----

    private void UpdateCamera(Keyboard keyboard, Mouse mouse)
    {
        if (viewCamera == null) return;
        if (keyboard != null)
        {
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            focus += Quaternion.Euler(0f, buildYaw, 0f) * new Vector3(x, 0f, z) * (panSpeed * Time.unscaledDeltaTime);
        }
        if (mouse != null)
        {
            Vector2 screen = mouse.position.ReadValue();
            if (mouse.middleButton.isPressed && !mouse.middleButton.wasPressedThisFrame && screen != lastMiddleDrag &&
                area.TryGroundPoint(viewCamera.ScreenPointToRay(lastMiddleDrag), out Vector3 from) &&
                area.TryGroundPoint(viewCamera.ScreenPointToRay(screen), out Vector3 to))
                focus += from - to;
            lastMiddleDrag = screen;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !PointerOverUI(screen))
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }
        focus = ClampFocus(focus);
        PlaceCamera();
    }

    private Vector3 ClampFocus(Vector3 value)
    {
        RectInt cells = area.Cells;
        return new Vector3(Mathf.Clamp(value.x, cells.xMin, cells.xMax), area.GroundHeight, Mathf.Clamp(value.z, cells.yMin, cells.yMax));
    }

    private void PlaceCamera()
    {
        if (viewCamera == null) return;
        Quaternion rotation = Quaternion.Euler(buildPitch, buildYaw, 0f);
        viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
    }

    private bool PointerOverUI(Vector2 screen)
    {
        if (EventSystem.current == null) return false;
        if (pointerData == null || pointerOwner != EventSystem.current)
        {
            pointerOwner = EventSystem.current;
            pointerData = new PointerEventData(pointerOwner);
        }
        pointerData.position = screen;
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointerData, uiHits);
        return uiHits.Count > 0;
    }
}
