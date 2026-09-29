using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 viewAngles = new Vector3(45f, 45f, 0f);
    [SerializeField, Min(0.1f)] private float distance = 12f;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;

    private Vector3 followPosition;
    private Vector3 followVelocity;
    private Quaternion fixedRotation;
    private Vector3 fixedOffset;
    private bool initialized;

    private void OnEnable()
    {
        initialized = false;
        followVelocity = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (!initialized)
        {
            fixedRotation = Quaternion.Euler(viewAngles);
            fixedOffset = fixedRotation * Vector3.back * distance;
            followPosition = target.position;
            initialized = true;
        }

        // Smooth the follow point; keep the viewing rotation and desired offset fixed.
        followPosition = Vector3.SmoothDamp(
            followPosition, target.position, ref followVelocity,
            smoothTime, Mathf.Infinity, Time.deltaTime);
        transform.SetPositionAndRotation(followPosition + fixedOffset, fixedRotation);
    }
}