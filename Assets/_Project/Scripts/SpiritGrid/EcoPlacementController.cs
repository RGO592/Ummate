using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Slots drag new tiles; placed cells drag existing tiles. Only a valid release mutates the grid.
// X / the remove button keep the existing click-and-sweep removal mode.
[DisallowMultipleComponent]
public sealed class EcoPlacementController : MonoBehaviour
{
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private EcoBuildAreaView buildArea;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private EnvironmentTileType selectedType = EnvironmentTileType.Wheat;

    [Header("Highlight")]
    [SerializeField] private Color canPlaceColor = new Color(0.3f, 0.95f, 0.35f);
    [SerializeField] private Color blockedColor = new Color(0.95f, 0.25f, 0.2f);
    [SerializeField] private Color pickedColor = new Color(1f, 0.85f, 0.15f);
    [SerializeField] private Color removeColor = new Color(1f, 0.35f, 0.1f);
    [SerializeField, Range(0.002f, 0.04f)] private float frameThicknessRatio = 0.012f;
    [SerializeField] private float frameHeight = 0.08f;

    private readonly EcoDragSession drag = new EcoDragSession();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private EcoBuildSlotDrag slotDragSource;
    private Transform hoverFrame, pickedFrame;
    private EcoDragPreview ghost;
    private readonly List<Renderer> hiddenSourceRenderers = new List<Renderer>();
    private readonly List<bool> sourceRenderingStates = new List<bool>();
    private Vector2 pointerScreen;
    private Vector3 pointerWorld;
    private bool pointerOverUI;
    private Renderer[] hoverRenderers;
    private bool hasHover, hasPlanePoint, lockedHover;
    private HexCoord hoverCoord;
    private int consumedFrame = -1;

    public EnvironmentTileType SelectedType => selectedType;
    public bool IsDragging => drag.IsActive;
    public bool HasHover => hasHover;
    public HexCoord HoverCoord => hoverCoord;
    public bool HasPicked => drag.IsActive && drag.IsMoving;
    public HexCoord PickedCoord => drag.Source;
    public EnvironmentTileType PickedType => HasPicked ? drag.Type : EnvironmentTileType.None;
    public bool HoverPlaceable { get; private set; }
    public bool RemoveMode { get; private set; }
    public string HoverStatus { get; private set; } = "";
    public string LastMessage { get; private set; } = "";
    private bool InputBlocked => buildArea != null && buildArea.InputBlocked;

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        hoverFrame = CreateFrame("HoverFrame", canPlaceColor);
        pickedFrame = CreateFrame("PickedFrame", pickedColor);
        hoverRenderers = hoverFrame.GetComponentsInChildren<Renderer>(true);
        HideHighlights();
    }

    private void OnDisable()
    {
        EndIntent();
        RemoveMode = false;
        hasHover = hasPlanePoint = lockedHover = false;
        HoverStatus = "";
        HideHighlights();
    }

    private void OnApplicationFocus(bool focused) { if (!focused) CancelPick(); }

    private void Update()
    {
        if (grid == null || viewCamera == null) return;
        if (InputBlocked)
        {
            if (drag.IsActive) CancelPick();
            buildArea.HandleModalKeyboard();
            hasHover = hasPlanePoint = lockedHover = false;
            HideHighlights();
            return;
        }
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) { CancelPick(); SetRemoveMode(false); return; }
            if (keyboard.xKey.wasPressedThisFrame) { ToggleRemoveMode(); return; }
            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
            { RemovePicked(); return; }
        }
        if (mouse == null) { if (drag.IsActive) CancelPick(); HideHighlights(); return; }
        Vector2 screen = mouse.position.ReadValue();
        if (mouse.rightButton.wasPressedThisFrame) { CancelPick(); SetRemoveMode(false); return; }
        if (consumedFrame == Time.frameCount) return;

        UpdatePointer(screen);
        if (drag.IsActive)
        {
            // UI drags end in OnEndDrag. World drags end here, even if released over UI/outside the grid.
            if (slotDragSource == null && mouse.leftButton.wasReleasedThisFrame) { CompleteDrop(screen); return; }
            if (slotDragSource == null && !mouse.leftButton.isPressed) { CancelPick(); return; }
            UpdateHighlights();
            return;
        }

        if (!PointerOverUI(screen))
        {
            if (lockedHover && mouse.leftButton.wasPressedThisFrame)
            {
                buildArea.ShowExpansionPrompt();
                HideHighlights();
                return;
            }
            if (RemoveMode && mouse.leftButton.isPressed) RemoveHovered();
            else if (!RemoveMode && hasHover && mouse.leftButton.wasPressedThisFrame)
            {
                EcoGridCell cell = grid.GetCell(hoverCoord);
                if (cell != null && !cell.IsEmpty)
                {
                    drag.BeginMove(cell.TileType, hoverCoord);
                    BeginPreview();
                    LastMessage = $"{EnvironmentTileTypes.DisplayName(cell.TileType)} 이동: 빈 칸에 놓으세요";
                    UpdatePointer(screen);
                }
                // Empty clicks never place a tile. Placement starts exclusively from a slot drag.
            }
        }
        UpdateHighlights();
    }

    public bool BeginSlotDrag(EcoBuildSlotDrag source, EnvironmentTileType type, Vector2 screen)
    {
        if (!isActiveAndEnabled || InputBlocked || source == null || type == EnvironmentTileType.None) return false;
        EndIntent();
        selectedType = type;
        RemoveMode = false;
        slotDragSource = source;
        drag.BeginPlacement(type);
        BeginPreview();
        LastMessage = $"{EnvironmentTileTypes.DisplayName(type)} 드래그 중";
        UpdatePointer(screen);
        UpdateHighlights();
        return true;
    }

    public void UpdateSlotDrag(EcoBuildSlotDrag source, Vector2 screen)
    {
        if (source != slotDragSource || !drag.IsActive || !isActiveAndEnabled) return;
        if (InputBlocked) { CancelPick(); return; }
        UpdatePointer(screen);
        UpdateHighlights();
    }

    public void EndSlotDrag(EcoBuildSlotDrag source, Vector2 screen)
    {
        if (source != slotDragSource || !drag.IsActive) return;
        if (!isActiveAndEnabled || InputBlocked) { CancelPick(); return; }
        CompleteDrop(screen);
    }

    public void CancelSlotDrag(EcoBuildSlotDrag source) { if (source == slotDragSource) CancelPick(); }

    private void CompleteDrop(Vector2 screen)
    {
        // Always raycast the release position; a cached hover can refer to another cell or the UI.
        UpdatePointer(screen);
        bool moving = drag.IsMoving;
        string name = EnvironmentTileTypes.DisplayName(drag.Type);
        bool committed;
        string reason;
        try { committed = drag.Drop(grid, hasHover && !InputBlocked, hoverCoord, out reason); }
        finally
        {
            ReleasePreview();
            if (slotDragSource != null) slotDragSource.ReleaseCapture();
            slotDragSource = null;
            consumedFrame = Time.frameCount;
        }
        LastMessage = committed ? $"{name} {(moving ? "이동" : "배치")}" : $"{(moving ? "이동" : "배치")} 취소: {reason}";
        HideHighlights();
    }

    public void SelectTypeByIndex(int number)
    {
        if (number >= 1 && number <= EnvironmentTileTypes.All.Length) SelectType(EnvironmentTileTypes.All[number - 1]);
    }

    public void SelectType(EnvironmentTileType type)
    {
        if (type == EnvironmentTileType.None || drag.IsActive) return;
        selectedType = type;
        RemoveMode = false;
        LastMessage = $"{EnvironmentTileTypes.DisplayName(type)} 슬롯을 원하는 칸으로 드래그하세요";
    }

    public void RemovePicked()
    {
        if (!HasPicked) return;
        HexCoord source = drag.Source;
        EndIntent();
        LastMessage = grid.TryRemove(source, out string reason) ? "환경물 제거" : reason;
        HideHighlights();
    }

    public void CancelPick()
    {
        if (drag.IsActive) LastMessage = drag.IsMoving ? "이동 취소: 원래 위치 유지" : "배치를 취소했습니다.";
        EndIntent();
        HideHighlights();
    }

    private void EndIntent()
    {
        ReleasePreview();
        drag.Cancel();
        if (slotDragSource != null) slotDragSource.ReleaseCapture();
        slotDragSource = null;
        consumedFrame = Time.frameCount;
    }

    public void ToggleRemoveMode() => SetRemoveMode(!RemoveMode);

    public void SetRemoveMode(bool on)
    {
        if (RemoveMode == on) return;
        CancelPick();
        RemoveMode = on;
        LastMessage = on ? "철거 모드: 클릭하거나 드래그해서 제거" : "철거 모드 종료";
    }

    private void RemoveHovered()
    {
        if (!hasHover) return;
        EcoGridCell cell = grid.GetCell(hoverCoord);
        if (cell == null || cell.IsEmpty) return;
        LastMessage = grid.TryRemove(hoverCoord, out string reason) ? "환경물 제거" : reason;
    }

    // Refresh after Update camera panning/zoom too, even if the pointer did not move.
    private void LateUpdate()
    {
        if (!drag.IsActive || viewCamera == null || Mouse.current == null) return;
        if (InputBlocked) { CancelPick(); return; }
        UpdatePointer(Mouse.current.position.ReadValue());
        UpdateHighlights();
    }

    private void UpdatePointer(Vector2 screen)
    {
        pointerScreen = screen;
        pointerOverUI = PointerOverUI(screen);
        hasHover = hasPlanePoint = lockedHover = false;
        HoverPlaceable = false;
        HoverStatus = drag.IsActive ? "배치 영역 밖 · 놓으면 취소" : "";
        Ray ray = viewCamera.ScreenPointToRay(screen);
        var plane = new Plane(grid.GridRoot.up, grid.GridRoot.position);
        if (plane.Raycast(ray, out float distance))
        {
            hasPlanePoint = true;
            pointerWorld = ray.GetPoint(distance);
            // UI blocks dropping, but never stops the carried ghost from following the pointer.
            bool inside = grid.WorldToGrid(pointerWorld, out hoverCoord);
            hasHover = !pointerOverUI && inside && grid.IsUnlocked(hoverCoord);
            lockedHover = !pointerOverUI && inside && grid.IsLocked(hoverCoord);
        }
        if (pointerOverUI) { if (drag.IsActive) HoverStatus = "UI 위 · 놓으면 취소"; return; }
        if (drag.IsActive)
        {
            HoverPlaceable = drag.CanDrop(grid, hasHover, hoverCoord, out string reason);
            HoverStatus = HoverPlaceable ? "놓으면 설치 / 이동" : $"불가: {reason} · 놓으면 취소";
        }
        else if (hasHover)
        {
            bool occupied = !grid.GetCell(hoverCoord).IsEmpty;
            HoverPlaceable = occupied;
            HoverStatus = RemoveMode ? (occupied ? "클릭 또는 드래그: 제거" : "빈 칸")
                : occupied ? "드래그해서 이동" : "하단 슬롯을 이 칸으로 드래그하세요";
        }
        else HoverStatus = lockedHover ? "잠긴 영역 · 클릭해서 개방 조건 확인" : "";
    }

    private void BeginPreview()
    {
        ReleasePreview();
        ghost = new EcoDragPreview(grid.GridRoot, drag.Type, grid.CellSize,
            buildArea != null ? buildArea.OverlayMaterial : null);
        // Hide only rendering. Cell data, scripts and connection state stay intact until Drop.
        GameObject source = drag.IsMoving ? grid.GetCell(drag.Source)?.Visual : null;
        if (source == null) return;
        foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            hiddenSourceRenderers.Add(renderer);
            sourceRenderingStates.Add(renderer.forceRenderingOff);
            renderer.forceRenderingOff = true;
        }
    }

    private void ReleasePreview()
    {
        ghost?.Dispose();
        ghost = null;
        for (int i = 0; i < hiddenSourceRenderers.Count; i++)
            if (hiddenSourceRenderers[i] != null) hiddenSourceRenderers[i].forceRenderingOff = sourceRenderingStates[i];
        hiddenSourceRenderers.Clear();
        sourceRenderingStates.Clear();
    }

    private void UpdateHighlights()
    {
        bool snap = hasHover || lockedHover;
        bool show = drag.IsActive ? snap : hasHover && HoverPlaceable;
        hoverFrame.gameObject.SetActive(show);
        if (show)
        {
            hoverFrame.position = grid.GridToWorld(hoverCoord);
            Color color = drag.IsActive ? (HoverPlaceable ? canPlaceColor : blockedColor)
                : RemoveMode ? removeColor : pickedColor;
            foreach (Renderer renderer in hoverRenderers) EcoVisualUtil.Paint(renderer, color);
        }
        if (drag.IsActive && ghost != null)
        {
            bool worldVisible = hasPlanePoint && !pointerOverUI;
            Vector3 position = snap ? grid.GridToWorld(hoverCoord) : pointerWorld;
            ghost.Show(pointerScreen, worldVisible, position + grid.GridRoot.up * frameHeight,
                grid.GridRoot.rotation, HoverPlaceable);
        }
        pickedFrame.gameObject.SetActive(HasPicked);
        if (HasPicked) pickedFrame.position = grid.GridToWorld(drag.Source);
    }

    private void HideHighlights()
    {
        if (hoverFrame != null) hoverFrame.gameObject.SetActive(false);
        if (pickedFrame != null) pickedFrame.gameObject.SetActive(false);
    }

    private Transform CreateFrame(string name, Color color)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(grid.GridRoot, false);
        Material material = buildArea != null ? buildArea.OverlayMaterial : null;
        HexMeshUtil.Create(pivot, "HexOutline", HexMeshUtil.Outline(grid.CellSize,
            grid.CellSize * frameThicknessRatio), frameHeight + 0.01f, color, material);
        return pivot;
    }

    private bool PointerOverUI(Vector2 screen)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        uiHits.Clear();
        events.RaycastAll(new PointerEventData(events) { position = screen }, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }

    private void OnDestroy()
    {
        ReleasePreview();
        if (hoverFrame != null) Destroy(hoverFrame.gameObject);
        if (pickedFrame != null) Destroy(pickedFrame.gameObject);
    }
}
