using UnityEngine;
using UnityEngine.EventSystems;

// Capture on press so the carried ghost exists before the first drag threshold is crossed.
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class EcoBuildSlotDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private EcoPlacementController placement;
    private EnvironmentTileType type;
    private bool dragging;
    private int pointerId;

    public void Initialize(EcoPlacementController controller, EnvironmentTileType tileType)
    {
        placement = controller;
        type = tileType;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || placement == null || dragging) return;
        pointerId = eventData.pointerId;
        dragging = placement.BeginSlotDrag(this, type, eventData.position);
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) { eventData.useDragThreshold = false; }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Never restart a press cancelled by Esc/right click while it was still above the slot.
        if (eventData.pointerId != pointerId) return;
        eventData.eligibleForClick = false;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragging && eventData.pointerId == pointerId && placement != null)
            placement.UpdateSlotDrag(this, eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData) { Finish(eventData); }
    public void OnEndDrag(PointerEventData eventData) { Finish(eventData); }

    // Controller cancellation (including focus loss) also releases the UI-side capture.
    public void ReleaseCapture() { dragging = false; }

    private void Finish(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !dragging || eventData.pointerId != pointerId) return;
        dragging = false;
        if (placement != null) placement.EndSlotDrag(this, eventData.position);
    }

    private void OnDisable()
    {
        dragging = false;
        if (placement != null) placement.CancelSlotDrag(this);
    }
}
