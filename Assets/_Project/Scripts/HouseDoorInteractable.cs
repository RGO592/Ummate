using UnityEngine;

[DisallowMultipleComponent]
public sealed class HouseDoorInteractable : Interactable
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private SleepTransition sleepTransition;

    public override void Interact(GameObject interactor)
    {
        if (sleepTransition != null) sleepTransition.TrySleep();
        else if (gameDay != null) gameDay.Sleep();
    }
}
