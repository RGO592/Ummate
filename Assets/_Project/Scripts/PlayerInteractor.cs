using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerInteractor : MonoBehaviour
{
    [SerializeField, Min(0f)] private float interactionDistance = 2f;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (Application.isFocused && keyboard != null && keyboard.gKey.wasPressedThisFrame)
            TryInteract();
    }

    public bool TryInteract()
    {
        Vector3 origin = transform.position;
        float range = Mathf.Max(0f, interactionDistance);
        float nearestDistanceSquared = range * range;
        Interactable nearest = null;

        // Query only on a key press. Collider surfaces determine interaction distance.
        Collider[] nearby = Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider candidate in nearby)
        {
            if (candidate.transform.IsChildOf(transform))
                continue;

            Interactable interactable = candidate.GetComponentInParent<Interactable>();
            if (interactable == null || !interactable.isActiveAndEnabled ||
                interactable.transform.IsChildOf(transform))
                continue;

            float distanceSquared = (candidate.ClosestPoint(origin) - origin).sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearest = interactable;
                nearestDistanceSquared = distanceSquared;
            }
        }

        if (nearest == null)
            return false;

        nearest.Interact(gameObject);
        return true;
    }
}