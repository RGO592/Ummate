using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Exploration camera: follows the player at a fixed angle. The mouse wheel zooms (distance along the view).
// Build modes disable this component and drive their own camera and zoom, so the two never share wheel input.
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 viewAngles = new Vector3(45f, 45f, 0f);
    [SerializeField, Min(0.1f), Tooltip("Starting distance; the wheel changes it within the limits below.")]
    private float distance = 12f;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;
    [Header("Wheel zoom")]
    [SerializeField, Min(0.1f)] private float minDistance = 6f;
    [SerializeField, Min(0.1f)] private float maxDistance = 30f;
    [SerializeField, Min(0.01f), Tooltip("Distance change per wheel notch.")]
    private float zoomStep = 1.5f;
    [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.08f;

    private Vector3 followPosition;
    private Vector3 followVelocity;
    private Quaternion fixedRotation;
    private bool initialized;
    private float targetDistance = -1f, currentDistance, zoomVelocity;
    private PlayerMovement targetMovement;

    private void OnEnable()
    {
        initialized = false;
        followVelocity = Vector3.zero;
    }

    private void Update()
    {
        if (targetDistance < 0f) targetDistance = currentDistance = Mathf.Clamp(distance, minDistance, Mathf.Max(minDistance, maxDistance));
        Mouse mouse = Mouse.current;
        if (mouse == null || !ZoomAllowed()) return;
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        // Wheel deltas vary by device (often 120 per notch): use only the direction, one step per event.
        targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, Mathf.Max(minDistance, maxDistance));
    }

    // Gameplay only: not over UI (lists scroll there), not while a window / sleep / travel has locked the player.
    private bool ZoomAllowed()
    {
        if (!Application.isFocused || Time.timeScale <= 0f || SceneTransitionService.IsTraveling || target == null) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        if (targetMovement == null) targetMovement = target.GetComponentInParent<PlayerMovement>();
        return targetMovement == null || targetMovement.isActiveAndEnabled;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (!initialized)
        {
            fixedRotation = Quaternion.Euler(viewAngles);
            followPosition = target.position;
            if (targetDistance < 0f) targetDistance = currentDistance = Mathf.Clamp(distance, minDistance, Mathf.Max(minDistance, maxDistance));
            initialized = true;
        }

        // Smooth the follow point and the zoom; keep the viewing rotation fixed.
        followPosition = Vector3.SmoothDamp(
            followPosition, target.position, ref followVelocity,
            smoothTime, Mathf.Infinity, Time.deltaTime);
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        transform.SetPositionAndRotation(followPosition + fixedRotation * Vector3.back * currentDistance, fixedRotation);
    }
}
