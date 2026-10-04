using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Turning speed in degrees per second.")]
    private float rotationSpeed = 540f;
    [SerializeField, Tooltip("Visual yaw correction in degrees. Use 90 when the model faces Visual -X.")]
    private float modelFacingOffset = 90f;

    private Rigidbody body;
    private Vector2 moveInput;
    private Transform movementCamera;
    private Transform visual;
    private Vector3 facingDirection;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        visual = transform.Find("Visual");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        moveInput = Vector2.zero;
        if (keyboard == null || !Application.isFocused)
            return;

        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        moveInput = Vector2.ClampMagnitude(new Vector2(x, z), 1f);
    }

    private void FixedUpdate()
    {
        // Keep gravity and ground collision response on the vertical axis.
        if (movementCamera == null && Camera.main != null)
            movementCamera = Camera.main.transform;
        Vector3 moveDirection = CalculateMoveDirection(moveInput, movementCamera);
        Vector3 velocity = body.linearVelocity;
        velocity.x = moveDirection.x * moveSpeed;
        velocity.z = moveDirection.z * moveSpeed;
        body.linearVelocity = velocity;

        facingDirection = moveDirection;

        // Preserve the old behaviour for players without a separate visual child.
        if (visual == null && moveDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            body.MoveRotation(Quaternion.RotateTowards(
                body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
    }

    private void LateUpdate()
    {
        // Turn the visible body independently of physics rotation constraints/interpolation.
        // No input means no rotation update, preserving the last visible heading.
        if (visual == null || moveInput.sqrMagnitude < 0.0001f || facingDirection.sqrMagnitude < 0.0001f)
            return;

        // Align the model's actual front with movement while preserving its local rotation.
        Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up)
            * Quaternion.Euler(0f, modelFacingOffset, 0f);
        visual.rotation = Quaternion.RotateTowards(
            visual.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private static Vector3 CalculateMoveDirection(Vector2 input, Transform view)
    {
        if (view == null)
            return new Vector3(input.x, 0f, input.y);

        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        // Also support a camera looking straight down without losing forward input.
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(view.up, Vector3.up);
        forward.Normalize();
        Vector3 right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
    }
}