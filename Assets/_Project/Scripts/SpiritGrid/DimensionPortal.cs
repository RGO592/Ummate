using UnityEngine;

// Dimension gate. F (PlayerInteractor) in front of it travels between the farm and the spirit realm.
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
    [SerializeField, Tooltip("Optional inner surface that slowly spins for readability.")]
    private Transform swirl;
    [SerializeField] private float swirlSpeed = 45f;

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
    }
}
