using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Exploration uses a player-centred orbit. Build modes disable this component and own the camera.
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
    [Header("Right-drag pitch")]
    [SerializeField, Range(1f, 85f)] private float minPitch = 30f;
    [SerializeField, Range(1f, 85f), Tooltip("Upper limit in degrees, capped at this scene's authored View Angles X.")]
    private float maxPitch = 50f;
    [SerializeField, Min(0.01f), Tooltip("Degrees of pitch per vertical pixel of mouse movement.")]
    private float pitchDragSpeed = 0.15f;

    private Vector3 followPosition;
    private Vector3 followVelocity;
    private float currentPitch;
    private bool draggingPitch;
    private bool initialized;
    private float targetDistance = -1f, currentDistance, zoomVelocity;
    private PlayerMovement targetMovement;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    private EventSystem pointerOwner;
    private PointerEventData pointerData;

    private void OnEnable()
    {
        initialized = false;
        followVelocity = Vector3.zero;
        draggingPitch = false;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) draggingPitch = false;
    }

    private void ReadCameraInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed) draggingPitch = false;
        if (mouse == null) return;
        float scroll = mouse.scroll.ReadValue().y;
        bool wheelMoved = Mathf.Abs(scroll) >= 0.01f;
        bool pressed = mouse.rightButton.wasPressedThisFrame && mouse.rightButton.isPressed;
        if (!wheelMoved && !pressed && !draggingPitch) return;
        if (!CameraInputAllowed(mouse))
        {
            // A drag that started on UI or reached a modal must begin again in the world.
            draggingPitch = false;
            return;
        }
        if (wheelMoved)
            targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, Mathf.Max(minDistance, maxDistance));

        if (pressed) draggingPitch = true;
        if (draggingPitch)
            currentPitch += mouse.delta.ReadValue().y * pitchDragSpeed;
    }

    private bool CameraInputAllowed(Mouse mouse)
    {
        if (!Application.isFocused || Time.timeScale <= 0f || SceneTransitionService.IsTraveling) return false;
        if (targetMovement == null) targetMovement = target.GetComponentInParent<PlayerMovement>();
        if (targetMovement != null && !targetMovement.isActiveAndEnabled) return false;

        // Query the current pointer position, including UI opened in this frame's Update.
        EventSystem events = EventSystem.current;
        if (events == null) return true;
        if (pointerOwner != events)
        {
            pointerOwner = events;
            pointerData = new PointerEventData(events);
        }
        pointerData.position = mouse.position.ReadValue();
        events.RaycastAll(pointerData, pointerHits);
        return pointerHits.Count == 0;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        float pitchLimit = Mathf.Min(maxPitch, viewAngles.x);
        // New scenes begin at their own quarter view. A resumed scene keeps its pitch and wheel distance.
        if (targetDistance < 0f)
        {
            currentPitch = pitchLimit;
            targetDistance = currentDistance = Mathf.Clamp(distance, minDistance, Mathf.Max(minDistance, maxDistance));
        }
        if (!initialized)
        {
            followPosition = target.position;
            initialized = true;
        }

        ReadCameraInput();
        currentPitch = Mathf.Clamp(currentPitch, Mathf.Min(minPitch, pitchLimit), pitchLimit);
        Quaternion rotation = Quaternion.Euler(currentPitch, viewAngles.y, viewAngles.z);
        followPosition = Vector3.SmoothDamp(
            followPosition, target.position, ref followVelocity,
            smoothTime, Mathf.Infinity, Time.deltaTime);
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        transform.SetPositionAndRotation(followPosition + rotation * Vector3.back * currentDistance, rotation);
    }
}
