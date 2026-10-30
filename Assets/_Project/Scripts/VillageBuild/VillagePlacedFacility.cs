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
    }
}
