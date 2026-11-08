using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerInteractor : MonoBehaviour
{
    [SerializeField, Min(0f)] private float interactionDistance = 2f;
    public float InteractionDistance => Mathf.Max(0f, interactionDistance);

    private void Update()
    {
        if (SceneTransitionService.IsTraveling) return;
        Keyboard keyboard = Keyboard.current;
        if (Application.isFocused && keyboard != null && keyboard.fKey.wasPressedThisFrame)
        {
            // Nothing to interact with: F also uses a usable selected item (potion). Other items stay silent as before.
            if (!TryInteract() && TryGetComponent(out PlayerQuickbar quickbar) && quickbar.isActiveAndEnabled)
                quickbar.TryUseSelected(notifyIfNotUsable: false);
        }
    }

    private Collider[] nearby = new Collider[32];

    public bool TryInteract()
    {
        if (SceneTransitionService.IsTraveling) return false;
        Interactable target = FindTarget();
        if (target == null)
            return false;

        target.Interact(gameObject);
        return true;
    }

    // The single target rule: F presses and the shared prompt (PlayerFeedbackUI) both use it,
    // so the prompt always names what F would actually interact with.
    public Interactable FindTarget()
    {
        Vector3 origin = transform.position;
        float range = Mathf.Max(0f, interactionDistance);
        float nearestDistanceSquared = range * range;
        Interactable nearest = null;

        // Polled every frame by the prompt, so reuse a buffer. A full buffer grows and queries again.
        // Collider surfaces determine interaction distance.
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(origin, range, nearby, ~0, QueryTriggerInteraction.Collide)) == nearby.Length)
            nearby = new Collider[nearby.Length * 2];
        for (int i = 0; i < count; i++)
        {
            Collider candidate = nearby[i];
            if (candidate.transform.IsChildOf(transform))
                continue;

            Interactable interactable = candidate.GetComponentInParent<Interactable>();
            // The larger safety volume is not an interaction target.
            if (interactable is FenceGateInteractable gate && candidate == gate.PassageTrigger)
                continue;
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
        return nearest;
    }
}
