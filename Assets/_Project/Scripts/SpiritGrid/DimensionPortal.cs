using UnityEngine;

// Dimension gate: a ring floating in the air. F (PlayerInteractor) in front of it travels between the farm and the spirit realm.
// Put the model under a "Visual" child; the collider on this object is the interaction / blocking volume.
[DisallowMultipleComponent]
public sealed class DimensionPortal : Interactable
{
    public enum PortalMode
    {
        EnterSpiritRealm,
        ReturnToFarm
    }

    [SerializeField] private PortalMode mode = PortalMode.EnterSpiritRealm;
    [SerializeField] private string spiritSceneName = "SpiritGridScene";
    [SerializeField] private string farmSceneName = "FarmScene";

    [Header("Visual (optional)")]
    [SerializeField, Tooltip("Ring part that spins around its facing axis.")]
    private Transform swirl;
    [SerializeField] private float swirlSpeed = 45f;
    [SerializeField, Tooltip("Part that bobs up and down so the portal reads as floating.")]
    private Transform floatingVisual;
    [SerializeField, Min(0f)] private float floatAmplitude = 0.2f;
    [SerializeField, Min(0f)] private float floatSpeed = 1.2f;

    private Vector3 floatingBase;

    private void Awake()
    {
        if (floatingVisual != null) floatingBase = floatingVisual.localPosition;
    }

    public override void Interact(GameObject interactor) => Use();

    // Also used by the "농장으로 돌아가기" UI button.
    public void Use()
    {
        if (mode == PortalMode.EnterSpiritRealm) SpiritRealmTravel.Enter(spiritSceneName);
        else SpiritRealmTravel.Return(farmSceneName);
    }

    private void Update()
    {
        if (swirl != null) swirl.Rotate(0f, 0f, swirlSpeed * Time.deltaTime, Space.Self);
        if (floatingVisual != null)
            floatingVisual.localPosition = floatingBase + Vector3.up * (Mathf.Sin(Time.time * floatSpeed) * floatAmplitude);
    }
}
