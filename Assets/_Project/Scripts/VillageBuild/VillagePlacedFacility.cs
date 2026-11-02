using UnityEngine;

// Added at runtime to every placed facility. Holds what a future save needs (Definition id + Cells)
// and lets the placement check tell placed facilities, tracked by cell, from other world colliders.
[DisallowMultipleComponent]
public sealed class VillagePlacedFacility : MonoBehaviour
{
    public VillageFacilityDefinition Definition { get; private set; }
    public RectInt Cells { get; private set; }
    public int RotationSteps { get; private set; }

    public void Place(VillageFacilityDefinition definition, RectInt cells, int rotationSteps)
    {
        Definition = definition;
        Cells = cells;
        RotationSteps = rotationSteps;
        // Quest objectives point at a placed facility by its definition id.
        if (!TryGetComponent(out QuestNavigationTarget target)) target = gameObject.AddComponent<QuestNavigationTarget>();
        target.Configure(definition.Id, new Vector2(cells.width, cells.height) * 0.5f);
    }
}
