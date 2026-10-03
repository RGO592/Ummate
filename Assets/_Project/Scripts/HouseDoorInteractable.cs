using UnityEngine;

[DisallowMultipleComponent]
public sealed class HouseDoorInteractable : Interactable
{
    [SerializeField] private GameDay gameDay;

    public override void Interact(GameObject interactor)
    {
        if (gameDay != null) gameDay.Sleep();
    }
}
