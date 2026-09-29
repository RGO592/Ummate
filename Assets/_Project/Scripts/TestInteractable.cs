using UnityEngine;

[DisallowMultipleComponent]
public sealed class TestInteractable : Interactable
{
    public override void Interact(GameObject interactor)
    {
        Debug.Log("TestInteractable 상호작용 실행", this);
    }
}