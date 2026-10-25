using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public abstract void Interact(GameObject interactor);

    // Short action label for the shared "[F] ..." prompt, e.g. "나무 베기". Reads current state only and
    // never changes it. Null or empty hides the prompt.
    public virtual string GetInteractionPrompt(GameObject interactor) => null;
}
