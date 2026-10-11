using UnityEngine;

[DisallowMultipleComponent]
public sealed class FenceGateInteractable : Interactable
{
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;
    [SerializeField, Range(0f, 180f)] private float openAngle = 90f;
    [SerializeField, Min(1f)] private float openSpeed = 240f;
    [SerializeField] private int openDirection = -1;
    [Header("Safe Auto Close")]
    [SerializeField] private BoxCollider passageTrigger;
    [SerializeField, Min(0f)] private float autoCloseDelay = 0.5f;

    private Quaternion leftClosed, rightClosed;
    private bool isOpen, enteredPassage, crossedPassage, manualCloseRequested;
    private Transform trackedPlayer;
    private int entrySide;
    private float clearTime;
    private readonly Collider[] occupants = new Collider[128];
    public bool IsOpen => isOpen;
    public BoxCollider PassageTrigger => passageTrigger;

    private void Awake()
    {
        if (leftDoor != null) leftClosed = leftDoor.localRotation;
        if (rightDoor != null) rightClosed = rightDoor.localRotation;
    }

    private void OnDisable()
    {
        enteredPassage = crossedPassage = manualCloseRequested = false;
        clearTime = 0;
        trackedPlayer = null;
    }

    public override void Interact(GameObject interactor)
    {
        if (isOpen)
        {
            // A manual close is also held until the whole swing area is empty.
            manualCloseRequested = true;
            return;
        }
        isOpen = true;
        manualCloseRequested = enteredPassage = crossedPassage = false;
        clearTime = 0;
        var player = interactor != null ? interactor.GetComponentInParent<PlayerInteractor>() : null;
        trackedPlayer = player != null ? player.transform : null;
        entrySide = PlayerSide();
    }

    private int PlayerSide()
    {
        if (trackedPlayer == null || passageTrigger == null) return 0;
        float z = passageTrigger.transform.InverseTransformPoint(trackedPlayer.position).z - passageTrigger.center.z;
        return z > 0.05f ? 1 : z < -0.05f ? -1 : 0;
    }

    private bool PassageOccupied(out bool playerInside)
    {
        playerInside = false;
        // Missing safety wiring must never allow an unchecked close.
        if (passageTrigger == null || !passageTrigger.enabled) return true;
        var t = passageTrigger.transform;
        Vector3 scale = t.lossyScale;
        Vector3 half = Vector3.Scale(passageTrigger.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Physics.SyncTransforms();
        int count = Physics.OverlapBoxNonAlloc(t.TransformPoint(passageTrigger.center), half,
            occupants, t.rotation, ~0, QueryTriggerInteraction.Collide);
        bool blocked = count == occupants.Length;
        for (int i = 0; i < count; i++)
        {
            Collider c = occupants[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;
            var player = c.GetComponentInParent<PlayerInteractor>();
            if (player != null)
            {
                blocked = true;
                if (trackedPlayer == player.transform) playerInside = true;
            }
            if (c.GetComponentInParent<ChickenFeeding>() != null || c.GetComponentInParent<CowFeeding>() != null)
                blocked = true;
        }
        return blocked;
    }

    private void Update()
    {
        bool blocked = PassageOccupied(out bool playerInside);
        if (isOpen)
        {
            int side = PlayerSide();
            if (playerInside)
            {
                enteredPassage = true;
                if (entrySide == 0) entrySide = side;
            }
            else if (enteredPassage)
            {
                // Leaving on the same side is a retreat, not a completed crossing.
                if (entrySide != 0 && side != 0 && side != entrySide) crossedPassage = true;
                enteredPassage = false;
                entrySide = side;
            }
            else if (!crossedPassage && side != 0) entrySide = side;

            if (blocked) clearTime = 0;
            else if (crossedPassage) clearTime += Time.deltaTime;
            if (!blocked && (manualCloseRequested || (crossedPassage && clearTime >= autoCloseDelay)))
            {
                isOpen = false;
                manualCloseRequested = crossedPassage = false;
                clearTime = 0;
            }
        }
        // Pause a closing leaf if an actor enters the safety area mid-swing.
        if (!isOpen && blocked) return;
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
