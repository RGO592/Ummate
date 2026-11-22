using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(100)]
public sealed class LevelingSceneReset : MonoBehaviour
{
    [SerializeField] private Rigidbody playerBody;
    [SerializeField] private CameraFollow followCamera;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Awake()
    {
        if (playerBody == null)
        {
            enabled = false;
            return;
        }
        startPosition = playerBody.position;
        startRotation = playerBody.rotation;
    }

    private void FixedUpdate()
    {
        if (playerBody.position.y >= -20f) return;
        playerBody.position = startPosition;
        playerBody.rotation = startRotation;
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;
        playerBody.transform.SetPositionAndRotation(startPosition, startRotation);
        Physics.SyncTransforms();
        if (followCamera != null && followCamera.isActiveAndEnabled)
        {
            // Reinitialize follow smoothing at the spawn while retaining the wheel zoom.
            followCamera.enabled = false;
            followCamera.enabled = true;
        }
    }
}
