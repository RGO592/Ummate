using UnityEngine;

// Sits on the non-rotating gate root (an interaction trigger spans the opening) and swings
// a double gate: each door leaf pivots on its own post and carries its own blocking collider.
[DisallowMultipleComponent]
public sealed class FenceGateInteractable : Interactable
{
    [SerializeField, Tooltip("Left door leaf. Pivot on the left post; the leaf extends toward local +X.")]
    private Transform leftDoor;
    [SerializeField, Tooltip("Right door leaf. Pivot on the right post; the leaf extends toward local -X.")]
    private Transform rightDoor;
    [SerializeField, Range(0f, 180f)] private float openAngle = 90f;
    [SerializeField, Min(1f), Tooltip("Rotation speed in degrees per second.")]
    private float openSpeed = 240f;
    [SerializeField, Tooltip("-1 swings both leaves toward local +Z (into the pen), +1 toward local -Z.")]
    private int openDirection = -1;

    private Quaternion leftClosed;
    private Quaternion rightClosed;
    private bool isOpen;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (leftDoor != null) leftClosed = leftDoor.localRotation;
        if (rightDoor != null) rightClosed = rightDoor.localRotation;
    }

    public override void Interact(GameObject interactor)
    {
        isOpen = !isOpen;
    }

    private void Update()
    {
        // Mirrored leaves: the same signed angle on the left becomes the opposite sign on the right.
        float angle = openAngle * Mathf.Sign(openDirection);
        Swing(leftDoor, leftClosed, angle);
        Swing(rightDoor, rightClosed, -angle);
    }

    private void Swing(Transform door, Quaternion closed, float angle)
    {
        if (door == null) return;
        Quaternion target = isOpen ? closed * Quaternion.Euler(0f, angle, 0f) : closed;
        door.localRotation = Quaternion.RotateTowards(door.localRotation, target, openSpeed * Time.deltaTime);
    }
}
