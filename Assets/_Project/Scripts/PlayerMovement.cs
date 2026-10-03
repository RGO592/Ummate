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
        Vector3 velocity = body.linearVelocity;
        velocity.x = moveInput.x * moveSpeed;
        velocity.z = moveInput.y * moveSpeed;
        body.linearVelocity = velocity;

        if (moveInput.sqrMagnitude > 0.0001f)
        {
            Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y);
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            Quaternion nextRotation = Quaternion.RotateTowards(
                body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            body.MoveRotation(nextRotation);
        }
    }
}