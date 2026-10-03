using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Turning speed in degrees per second.")]
    private float rotationSpeed = 540f;

    private Rigidbody body;
    private Vector2 moveInput;
    private Transform movementCamera;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
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

        if (moveDirection.sqrMagnitude > 0.0001f)
        {

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            Quaternion nextRotation = Quaternion.RotateTowards(
                body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            body.MoveRotation(nextRotation);
        }
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