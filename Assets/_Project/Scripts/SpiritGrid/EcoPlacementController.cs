using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Build-mode input for the eco grid: 1~5 or the build bar select a tile kind, left click places / picks up / drops,
// right click or Esc cancels, Delete (or Backspace) removes the picked-up tile.
// Enabled only while build mode is on; disabling hides highlights and cancels the pick.
[DisallowMultipleComponent]
public sealed class EcoPlacementController : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField, Tooltip("Empty uses Camera.main.")] private Camera viewCamera;
    [SerializeField] private EnvironmentTileType selectedType = EnvironmentTileType.Wheat;

    [Header("Highlight")]
    [SerializeField] private Color canPlaceColor = new Color(0.3f, 0.95f, 0.35f);
    [SerializeField] private Color blockedColor = new Color(0.95f, 0.25f, 0.2f);
    [SerializeField] private Color pickedColor = new Color(1f, 0.85f, 0.15f);
    [SerializeField, Range(0.01f, 0.2f), Tooltip("Frame bar width as a fraction of the cell size.")]
    private float frameThicknessRatio = 0.04f;
    [SerializeField, Tooltip("Height of the outline frame above the ground.")]
    private float frameHeight = 0.35f;

    private Transform hoverFrame;
    private Transform pickedFrame;
    private Renderer[] hoverRenderers;
    private bool hasHover;
    private Vector2Int hoverCoord;
    private bool hasPicked;
    private Vector2Int pickedCoord;

    public EnvironmentTileType SelectedType => selectedType;
    public bool HasHover => hasHover;
    public Vector2Int HoverCoord => hoverCoord;
    public bool HasPicked => hasPicked;
    public Vector2Int PickedCoord => pickedCoord;
    public EnvironmentTileType PickedType => hasPicked && grid != null ? grid.GetCell(pickedCoord).TileType : EnvironmentTileType.None;
    public bool HoverPlaceable { get; private set; }
    public string HoverStatus { get; private set; } = "";
    public string LastMessage { get; private set; } = "";

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        hoverFrame = CreateFrame("HoverFrame", canPlaceColor);
        pickedFrame = CreateFrame("PickedFrame", pickedColor);
        hoverRenderers = hoverFrame.GetComponentsInChildren<Renderer>(true);
    }

    private void OnDisable()
    {
        hasPicked = false;
        hasHover = false;
        HoverStatus = "";
        if (hoverFrame != null) hoverFrame.gameObject.SetActive(false);
        if (pickedFrame != null) pickedFrame.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (grid == null || viewCamera == null) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null) HandleKeyboard(keyboard);
        UpdateHover(mouse);
        if (mouse != null && !PointerOverUI())
        {
            if (mouse.leftButton.wasPressedThisFrame) HandleLeftClick();
            else if (mouse.rightButton.wasPressedThisFrame) CancelPick();
        }
        UpdateHighlights();
    }

    // UI buttons can call this with 1~5.
    public void SelectTypeByIndex(int number)
    {
        if (number < 1 || number > EnvironmentTileTypes.All.Length) return;
        SelectType(EnvironmentTileTypes.All[number - 1]);
    }

    public void SelectType(EnvironmentTileType type)
    {
        if (type == EnvironmentTileType.None) return;
        selectedType = type;
        hasPicked = false;   // choosing a new kind returns to placing
        LastMessage = $"{EnvironmentTileTypes.DisplayName(type)} 선택";
    }

    // Build bar button / Delete key: removes the picked-up tile.
    public void RemovePicked()
    {
        if (!hasPicked) { LastMessage = "제거할 환경물을 먼저 클릭해 선택하세요."; return; }
        EnvironmentTileType type = grid.GetCell(pickedCoord).TileType;
        if (grid.TryRemove(pickedCoord, out string reason))
        {
            LastMessage = $"{EnvironmentTileTypes.DisplayName(type)} 제거";
            hasPicked = false;
        }
        else LastMessage = reason;
    }

    public void CancelPick()
    {
        if (!hasPicked) return;
        hasPicked = false;
        LastMessage = "선택을 취소했습니다.";
    }

    private void HandleKeyboard(Keyboard keyboard)
    {
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SelectTypeByIndex(1);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SelectTypeByIndex(2);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SelectTypeByIndex(3);
        else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) SelectTypeByIndex(4);
        else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) SelectTypeByIndex(5);
        if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) RemovePicked();
        if (keyboard.escapeKey.wasPressedThisFrame) CancelPick();
    }

    private void HandleLeftClick()
    {
        if (!hasHover) return;
        EcoGridCell cell = grid.GetCell(hoverCoord);
        if (hasPicked)
        {
            if (hoverCoord == pickedCoord) { CancelPick(); return; }
            if (!cell.IsEmpty) { Pick(hoverCoord); return; }   // switch the picked tile
            EnvironmentTileType moving = grid.GetCell(pickedCoord).TileType;
            if (grid.TryMove(pickedCoord, hoverCoord, out string moveReason))
            {
                hasPicked = false;
                LastMessage = $"{EnvironmentTileTypes.DisplayName(moving)} 이동";
            }
            else LastMessage = moveReason;
            return;
        }
        if (!cell.IsEmpty) { Pick(hoverCoord); return; }
        if (grid.TryPlace(selectedType, hoverCoord, out string reason))
            LastMessage = $"{EnvironmentTileTypes.DisplayName(selectedType)} 배치";
        else LastMessage = reason;
    }

    private void Pick(Vector2Int coord)
    {
        hasPicked = true;
        pickedCoord = coord;
        LastMessage = $"{EnvironmentTileTypes.DisplayName(grid.GetCell(coord).TileType)} 선택됨";
    }

    private void UpdateHover(Mouse mouse)
    {
        hasHover = false;
        HoverPlaceable = false;
        HoverStatus = "";
        if (mouse == null || PointerOverUI()) return;
        Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (!grid.RaycastGrid(ray, out hoverCoord)) return;
        hasHover = true;
        string reason = null;
        if (hasPicked)
        {
            HoverPlaceable = hoverCoord == pickedCoord || grid.CanMove(pickedCoord, hoverCoord, out reason);
            HoverStatus = hoverCoord == pickedCoord ? "선택된 환경물" : HoverPlaceable ? "여기로 이동" : $"이동 불가: {reason}";
        }
        else if (!grid.GetCell(hoverCoord).IsEmpty)
            HoverStatus = "클릭하면 이동 / 제거할 환경물로 선택";
        else
        {
            HoverPlaceable = grid.CanPlace(selectedType, hoverCoord, out reason);
            HoverStatus = HoverPlaceable ? "설치 가능" : $"설치 불가: {reason}";
        }
    }

    private void UpdateHighlights()
    {
        bool showHover = hasHover && !(hasPicked && hoverCoord == pickedCoord);
        hoverFrame.gameObject.SetActive(showHover);
        if (showHover)
        {
            hoverFrame.position = grid.GridToWorld(hoverCoord);
            Color color = HoverPlaceable ? canPlaceColor : blockedColor;
            foreach (Renderer r in hoverRenderers) EcoVisualUtil.Paint(r, color);
        }
        pickedFrame.gameObject.SetActive(hasPicked);
        if (hasPicked) pickedFrame.position = grid.GridToWorld(pickedCoord);
    }

    // Outline frame (four bars) floating above the cell so it stays visible over tiles and water.
    private Transform CreateFrame(string name, Color color)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(grid != null ? grid.GridRoot : transform, false);
        float half = (grid != null ? grid.CellSize : 1f) * 0.5f;
        float t = half * 2f * frameThicknessRatio, y0 = frameHeight, y1 = frameHeight + 0.1f;
        EcoVisualUtil.Box(pivot.transform, "N", new Vector3(-half, y0, half - t), new Vector3(half, y1, half), color);
        EcoVisualUtil.Box(pivot.transform, "S", new Vector3(-half, y0, -half), new Vector3(half, y1, -half + t), color);
        EcoVisualUtil.Box(pivot.transform, "E", new Vector3(half - t, y0, -half + t), new Vector3(half, y1, half - t), color);
        EcoVisualUtil.Box(pivot.transform, "W", new Vector3(-half, y0, -half + t), new Vector3(-half + t, y1, half - t), color);
        foreach (Renderer r in pivot.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pivot.SetActive(false);
        return pivot.transform;
    }

    private static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
